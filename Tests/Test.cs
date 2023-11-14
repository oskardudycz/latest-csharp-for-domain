using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

using static ShoppingCartCommand;
using static ShoppingCartEvent;
using static ShoppingCart;

// Types Aliases
using ProductItems = ImmutableDictionary<string, int>;
using ProductItem = (Guid ProductId, int Quantity);
using PricedProductItem = (Guid ProductId, int Quantity, decimal Price);

// Commands
public abstract record ShoppingCartCommand
{
    public record AddProduct(
        ProductItem ProductItem,
        Guid? ClientId = null
    ): ShoppingCartCommand;

    public record RemoveProduct(
        PricedProductItem ProductItem
    ): ShoppingCartCommand;

    public record Confirm(
        Guid ClientId
    ): ShoppingCartCommand;

    public record Cancel: ShoppingCartCommand;

    private ShoppingCartCommand() { }
}

public abstract record ShoppingCartEvent
{
    public record Opened(
        Guid? ClientId,
        DateTimeOffset OpenedAt
    ): ShoppingCartEvent;

    public record ProductAdded(
        PricedProductItem ProductItem,
        DateTimeOffset AddedAt
    ): ShoppingCartEvent;

    public record ProductRemoved(
        PricedProductItem ProductItem,
        DateTimeOffset RemovedAt
    ): ShoppingCartEvent;

    public record Confirmed(
        Guid ClientId,
        DateTimeOffset ConfirmedAt
    ): ShoppingCartEvent;

    public record Cancelled(
        DateTimeOffset CanceledAt
    ): ShoppingCartEvent;

    private ShoppingCartEvent() { }
}

public abstract record ShoppingCart
{
    public record Empty: ShoppingCart;

    public record Pending(ProductItems ProductItems): ShoppingCart;

    public record Closed: ShoppingCart;

    private ShoppingCart() { }
}

// Primary constructor & TimeProvider
// Then Collection expressions and advanced pattern matching
public class ShoppingCartDecider(
    IProductPriceCalculator priceCalculator,
    TimeProvider timeProvider
)
{
    public ShoppingCartEvent[] Decide(ShoppingCartCommand command, ShoppingCart state) =>
        (state, command) switch
        {
            (Empty, AddProduct (var productItem, var clientId)) =>
            [
                new Opened(clientId, Now),
                new ProductAdded(priceCalculator.Calculate(productItem).Single(), Now)
            ],

            (Pending, AddProduct (var productItem, _)) =>
            [
                new ProductAdded(priceCalculator.Calculate(productItem).Single(), Now)
            ],

            (Pending pending, RemoveProduct((var productId, var toRemove, var price) pricedProductItem)) =>
                pending.ProductItems.TryGetValue($"{productId}{price}", out var quantity) && quantity >= toRemove
                    ?
                    [
                        new ProductRemoved(pricedProductItem, Now)
                    ]
                    : throw new InvalidOperationException("Not enough Products!"),

            (Pending, Confirm(var clientId)) =>
            [
                new Confirmed(clientId, Now)
            ],

            (Pending, Cancel) =>
            [
                new Cancelled(Now)
            ],

            (Closed, Confirm) => [],

            (Closed, Cancel) => [],

            _ => throw new InvalidOperationException(nameof(command))
        };

    private DateTimeOffset Now => timeProvider.GetLocalNow();
}

public class ShoppingCartModel
{
    public required Guid Id { get; init; }
    public Guid? ClientId { get; set; }
    public required ShoppingCartStatus Status { get; set; }
    public List<PricedProductItem> ProductItems { get; init; } = [];
    public required DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    public class PricedProductItem
    {
        public required Guid ProductId { get; init; }
        public required int Quantity { get; set; }
        public required decimal Price { get; init; }
        public required DateTimeOffset AddedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    public enum ShoppingCartStatus
    {
        Pending,
        Confirmed
    }
}

public interface IShoppingCartRepository
{
    ValueTask<ShoppingCartModel?> Find(Guid id, CancellationToken ct);

    Task Store(Guid id, ShoppingCartModel? current, ShoppingCartEvent[] events, CancellationToken ct);

    async Task GetAndStore(Guid id, Func<ShoppingCartModel?, ShoppingCartEvent[]> handle, CancellationToken ct)
    {
        var state = await Find(id, ct);

        var events = handle(state);

        await Store(id, state, events, ct);
    }
}

public class ShoppingClassCommandHandler(
    ShoppingClassDecider decider,
    IShoppingCartRepository repository
)
{
    public Task Handle(Guid id, ShoppingCartCommand command, CancellationToken ct) =>
        repository.GetAndStore(
            id,
            model => decider.Decide(command, Map(model)),
            ct
        );

    private ShoppingCart Map(ShoppingCartModel? model)
    {
        if (model == null)
            return new Empty();

        switch (model.Status)
        {
            case ShoppingCartModel.ShoppingCartStatus.Pending:
                return new Pending(
                    model.ProductItems.ToImmutableDictionary(ks => $"{ks.ProductId}{ks.Price}", vs => vs.Quantity)
                );

            case ShoppingCartModel.ShoppingCartStatus.Confirmed:
                return new Closed();

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

public class ECommerceDBContext: DbContext
{
    public DbSet<ShoppingCartModel> ShoppingCarts { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShoppingCartModel>()
            .ToTable("ShoppingCarts")
            .OwnsMany(b => b.ProductItems, a =>
            {
                a.ToTable("ProductItems");
                a.WithOwner().HasForeignKey("ShoppingCartId");
                a.HasKey("ProductId", "ShoppingCartId");
            });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseInMemoryDatabase("ECommerceTest");
}

public class ShoppingCartRepository(ECommerceDBContext dbContext): IShoppingCartRepository
{
    public ValueTask<ShoppingCartModel?> Find(Guid id, CancellationToken ct) =>
        dbContext.FindAsync<ShoppingCartModel>([id], ct);

    public Task Store(Guid id, ShoppingCartModel? current, ShoppingCartEvent[] events, CancellationToken ct)
    {
        foreach (var @event in events)
        {
            switch (@event, current)
            {
                case (Opened(var clientId, var openedAt), null):
                    dbContext.Add(new ShoppingCartModel
                    {
                        Id = id,
                        ClientId = clientId,
                        Status = ShoppingCartModel.ShoppingCartStatus.Pending,
                        OpenedAt = openedAt
                    });
                    break;
                case (ProductAdded(var (productId, quantity, price), var addedAt), not null):
                    var toUpdate = current.ProductItems.SingleOrDefault(p => p.ProductId == productId);

                    if (toUpdate == null)
                    {
                        current.ProductItems.Add(new ShoppingCartModel.PricedProductItem
                        {
                            ProductId = productId, Quantity = quantity, Price = price, AddedAt = addedAt,
                        });
                    }
                    else
                    {
                        toUpdate.Quantity += quantity;
                        toUpdate.UpdatedAt = addedAt;
                    }

                    break;
                case (ProductRemoved(var (productId, quantity, price), var removedAt), not null):
                    var existing = current.ProductItems.Single(p => p.ProductId == productId);

                    if (existing.Quantity - quantity == 0)
                    {
                        current.ProductItems.Remove(existing);
                    }
                    else
                    {
                        existing.Quantity -= quantity;
                        existing.UpdatedAt = removedAt;
                    }

                    break;
                case (Confirmed (var clientId, var confirmedAt), not null):
                    current.Status = ShoppingCartModel.ShoppingCartStatus.Confirmed;
                    current.ClientId = clientId;
                    current.ConfirmedAt = confirmedAt;
                    dbContext.ShoppingCarts.Update(current);
                    break;
                case (Cancelled, not null):
                    dbContext.ShoppingCarts.Remove(current);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(@event));
            }
        }

        return dbContext.SaveChangesAsync(ct);
    }
}

public interface IProductPriceCalculator
{
    IReadOnlyList<PricedProductItem> Calculate(params ProductItem[] productItems);
}

public class DummyProductPriceCalculator(decimal price): IProductPriceCalculator
{
    public IReadOnlyList<PricedProductItem> Calculate(params ProductItem[] productItems) =>
        productItems.Select(pi => new PricedProductItem(pi.ProductId, pi.Quantity, price)).ToArray();
}

public class Test
{
    [Fact]
    public void GivenEmptyShoppingCart_WhenAddProduct_ThenReturnsOpenedAndProductAddedEvents()
    {
        //Given
        var price = (decimal)new Random().NextDouble() * 100m;
        var now = DateTimeOffset.Now;

        var cardId = Guid.NewGuid();
        var productItem = new ProductItem(Guid.NewGuid(), 10);

        var state = new Empty();

        var decider = new ShoppingClassDecider(new DummyProductPriceCalculator(price), new FakeTimeProvider(now));

        //When
        var result = decider.Decide(new AddProduct(productItem), state);
        
        result.Should().Equal([
            new Opened(null, now),
            new ProductAdded(new PricedProductItem(productItem.ProductId, productItem.Quantity, price), now)
        ]);
    }
}
