using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

using static ShoppingCartCommand;
using static ShoppingCartEvent;
using static ShoppingCart;
using ProductItems = ImmutableDictionary<string, int>;

public record ProductItem(Guid ProductId, int Quantity);

public record PricedProductItem(Guid ProductId, int Quantity, decimal Price):
    ProductItem(ProductId, Quantity);

// Commands
public abstract record ShoppingCartCommand
{
    public record AddProduct(
        Guid CartId,
        ProductItem ProductItem,
        Guid? ClientId = null
    ): ShoppingCartCommand;

    public record RemoveProduct(
        Guid CartId,
        PricedProductItem ProductItem
    ): ShoppingCartCommand;

    public record Confirm(
        Guid CartId,
        Guid ClientId
    ): ShoppingCartCommand;

    public record Cancel(
        Guid CartId
    ): ShoppingCartCommand;
}

public abstract record ShoppingCartEvent
{
    public record Opened(
        Guid CartId,
        Guid? ClientId,
        DateTimeOffset OpenedAt
    ): ShoppingCartEvent;

    public record ProductAdded(
        Guid CartId,
        PricedProductItem ProductItem,
        DateTimeOffset AddedAt
    ): ShoppingCartEvent;

    public record ProductRemoved(
        Guid CartId,
        PricedProductItem ProductItem,
        DateTimeOffset RemovedAt
    ): ShoppingCartEvent;

    public record Confirmed(
        Guid CartId,
        Guid ClientId,
        DateTimeOffset ConfirmedAt
    ): ShoppingCartEvent;

    public record Cancelled(
        Guid CartId,
        DateTimeOffset CanceledAt
    ): ShoppingCartEvent;
}

public abstract record ShoppingCart
{
    public record Empty: ShoppingCart;

    public record Pending(ProductItems ProductItems): ShoppingCart;

    public record Closed: ShoppingCart;
}

public class ShoppingClassDecider(
    IProductPriceCalculator priceCalculator,
    TimeProvider timeProvider)
{
    public ShoppingCartEvent[] Decide(ShoppingCartCommand command, ShoppingCart state) =>
        (state, command) switch
        {
            (Empty, AddProduct (var cartId, var productItem, var clientId)) =>
            [
                new Opened(cartId, clientId, Now),
                new ProductAdded(cartId, priceCalculator.Calculate(productItem).Single(), Now)
            ],

            (Pending, AddProduct (var cartId, var productItem, _)) =>
            [
                new ProductAdded(cartId, priceCalculator.Calculate(productItem).Single(), Now)
            ],

            (Pending pending, RemoveProduct(var cartId, var pricedProductItem)) =>
                pending.ProductItems
                    .TryGetValue($"{pricedProductItem.ProductId}{pricedProductItem.Price}", out var quantity)
                && quantity >= pricedProductItem.Quantity
                    ?
                    [
                        new ProductRemoved(cartId, pricedProductItem, Now)
                    ]
                    : throw new InvalidOperationException("Not enough Products!"),

            (Pending, Confirm(var cartId, var clientId)) =>
            [
                new Confirmed(cartId, clientId, Now)
            ],

            (Pending, Cancel(var cartId)) =>
            [
                new Cancelled(cartId, Now)
            ],

            (Closed, Confirm) => [],

            (Closed, Cancel) => [],

            (_, _) => throw new InvalidOperationException(nameof(command))
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
    }

    public enum ShoppingCartStatus
    {
        Pending,
        Confirmed,
        Canceled
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
            case ShoppingCartModel.ShoppingCartStatus.Canceled:
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
            switch ((current, @event))
            {
                case (null, Opened(_, var clientId, var openedAt)):
                    dbContext.Add(new ShoppingCartModel
                    {
                        Id = id,
                        ClientId = clientId,
                        Status = ShoppingCartModel.ShoppingCartStatus.Pending,
                        OpenedAt = openedAt
                    });
                    break;
                case (not null, Confirmed (_, var clientId, var confirmedAt)):
                    current.Status = ShoppingCartModel.ShoppingCartStatus.Confirmed;
                    current.ClientId = clientId;
                    current.ConfirmedAt = confirmedAt;
                    dbContext.ShoppingCarts.Update(current);
                    break;
                case (not null, Cancelled (_, var canceledAt)):
                    current.Status = ShoppingCartModel.ShoppingCartStatus.Canceled;
                    current.ConfirmedAt = canceledAt;
                    dbContext.ShoppingCarts.Update(current);
                    break;
                case (not null, ProductAdded(_, var productItem, _)):
                    current.ProductItems
                        .Single(p => p.ProductId == productItem.ProductId)
                        .Quantity += productItem.Quantity;
                    break;
                case (not null, ProductRemoved(_, var productItem, _)):
                    var existing = current!.ProductItems.Single(p => p.ProductId == productItem.ProductId);

                    if (existing.Quantity - productItem.Quantity == 0)
                        current.ProductItems.Remove(existing);
                    else
                        existing.Quantity -= productItem.Quantity;

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
        var result = decider.Decide(new AddProduct(cardId, productItem), state);


        result.Should().Equal([
            new Opened(cardId, null, now),
            new ProductAdded(cardId, new PricedProductItem(productItem.ProductId, productItem.Quantity, price), now)
        ]);
    }
}
