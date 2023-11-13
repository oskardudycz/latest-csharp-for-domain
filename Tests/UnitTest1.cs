namespace Tests;

public static class ListExtensions
{
    public static IList<T> Replace<T>(this IList<T> list, T existingElement, T replacement)
    {
        var indexOfExistingItem = list.IndexOf(existingElement);

        if (indexOfExistingItem == -1)
            throw new ArgumentOutOfRangeException(nameof(existingElement), "Element was not found");

        list[indexOfExistingItem] = replacement;

        return list;
    }
}

public interface IProductPriceCalculator
{
    IReadOnlyList<PricedProductItem> Calculate(params ProductItem[] productItems);
}

// Commands

public record OpenShoppingCart(
    Guid CartId,
    Guid ClientId
)
{
    public static OpenShoppingCart Create(Guid? cartId, Guid? clientId)
    {
        if (cartId == null || cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));
        if (clientId == null || clientId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(clientId));

        return new OpenShoppingCart(cartId.Value, clientId.Value);
    }
}

public record AddProduct(
    Guid CartId,
    ProductItem ProductItem
)
{
    public static AddProduct Create(Guid cartId, ProductItem productItem)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));

        return new AddProduct(cartId, productItem);
    }
}

public record RemoveProduct(
    Guid CartId,
    PricedProductItem ProductItem
)
{
    public static RemoveProduct Create(Guid cartId, PricedProductItem productItem)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));

        return new RemoveProduct(cartId, productItem);
    }
}

public record ConfirmShoppingCart(
    Guid CartId
)
{
    public static ConfirmShoppingCart Create(Guid cartId)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));

        return new ConfirmShoppingCart(cartId);
    }
}

public record CancelShoppingCart(
    Guid CartId
)
{
    public static CancelShoppingCart Create(Guid cartId)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));

        return new CancelShoppingCart(cartId);
    }
}

// Events

public record ShoppingCartOpened(
    Guid CartId,
    Guid ClientId
)
{
    public static ShoppingCartOpened Create(Guid cartId, Guid clientId)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));
        if (clientId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(clientId));

        return new ShoppingCartOpened(cartId, clientId);
    }
}

public record ProductAdded(
    Guid CartId,
    PricedProductItem ProductItem
)
{
    public static ProductAdded Create(Guid cartId, PricedProductItem productItem)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));

        return new ProductAdded(cartId, productItem);
    }
}

public record ProductRemoved(
    Guid CartId,
    PricedProductItem ProductItem
)
{
    public static ProductRemoved Create(Guid cartId, PricedProductItem productItem)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));

        return new ProductRemoved(cartId, productItem);
    }
}

public record ShoppingCartConfirmed(
    Guid CartId,
    DateTime ConfirmedAt
)
{
    public static ShoppingCartConfirmed Create(Guid cartId, DateTime confirmedAt)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));
        if (confirmedAt == default)
            throw new ArgumentOutOfRangeException(nameof(confirmedAt));

        return new ShoppingCartConfirmed(cartId, confirmedAt);
    }
}

public record ShoppingCartCanceled(
    Guid CartId,
    DateTime CanceledAt
)
{
    public static ShoppingCartCanceled Create(Guid cartId, DateTime canceledAt)
    {
        if (cartId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(cartId));
        if (canceledAt == default)
            throw new ArgumentOutOfRangeException(nameof(canceledAt));

        return new ShoppingCartCanceled(cartId, canceledAt);
    }
}


// Value Objects
public record PricedProductItem
{
    public Guid ProductId => ProductItem.ProductId;

    public int Quantity => ProductItem.Quantity;

    public decimal UnitPrice { get; }

    public decimal TotalPrice => Quantity * UnitPrice;
    public ProductItem ProductItem { get; }

    private PricedProductItem(ProductItem productItem, decimal unitPrice)
    {
        ProductItem = productItem;
        UnitPrice = unitPrice;
    }

    public static PricedProductItem Create(Guid? productId, int? quantity, decimal? unitPrice)
    {
        return Create(
            ProductItem.From(productId, quantity),
            unitPrice
        );
    }

    public static PricedProductItem Create(ProductItem productItem, decimal? unitPrice)
    {
        return unitPrice switch
        {
            null => throw new ArgumentNullException(nameof(unitPrice)),
            <= 0 => throw new ArgumentOutOfRangeException(nameof(unitPrice),
                "Unit price has to be positive number"),
            _ => new PricedProductItem(productItem, unitPrice.Value)
        };
    }

    public bool MatchesProductAndPrice(PricedProductItem pricedProductItem)
    {
        return ProductId == pricedProductItem.ProductId && UnitPrice == pricedProductItem.UnitPrice;
    }

    public PricedProductItem MergeWith(PricedProductItem pricedProductItem)
    {
        if (!MatchesProductAndPrice(pricedProductItem))
            throw new ArgumentException("Product or price does not match.");

        return new PricedProductItem(ProductItem.MergeWith(pricedProductItem.ProductItem), UnitPrice);
    }

    public PricedProductItem Subtract(PricedProductItem pricedProductItem)
    {
        if (!MatchesProductAndPrice(pricedProductItem))
            throw new ArgumentException("Product or price does not match.");

        return new PricedProductItem(ProductItem.Subtract(pricedProductItem.ProductItem), UnitPrice);
    }

    public bool HasEnough(int quantity)
    {
        return ProductItem.HasEnough(quantity);
    }

    public bool HasTheSameQuantity(PricedProductItem pricedProductItem)
    {
        return ProductItem.HasTheSameQuantity(pricedProductItem.ProductItem);
    }
}

public record ProductItem
{
    public Guid ProductId { get; }

    public int Quantity { get; }

    private ProductItem(Guid productId, int quantity)
    {
        ProductId = productId;
        Quantity = quantity;
    }

    public static ProductItem From(Guid? productId, int? quantity)
    {
        if (!productId.HasValue)
            throw new ArgumentNullException(nameof(productId));

        return quantity switch
        {
            null => throw new ArgumentNullException(nameof(quantity)),
            <= 0 => throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity has to be a positive number"),
            _ => new ProductItem(productId.Value, quantity.Value)
        };
    }

    public ProductItem MergeWith(ProductItem productItem)
    {
        if (!MatchesProduct(productItem))
            throw new ArgumentException("Product does not match.");

        return From(ProductId, Quantity + productItem.Quantity);
    }

    public ProductItem Subtract(ProductItem productItem)
    {
        if (!MatchesProduct(productItem))
            throw new ArgumentException("Product does not match.");

        return From(ProductId, Quantity - productItem.Quantity);
    }

    public bool MatchesProduct(ProductItem productItem)
    {
        return ProductId == productItem.ProductId;
    }

    public bool HasEnough(int quantity)
    {
        return Quantity >= quantity;
    }

    public bool HasTheSameQuantity(ProductItem productItem)
    {
        return Quantity == productItem.Quantity;
    }
}

// Aggregate

public enum ShoppingCartStatus
{
    Pending = 1,
    Confirmed = 2,
    Canceled = 4
}

public class ShoppingCart
{
    public Guid Id { get; private set; }

    public Guid ClientId { get; private set; }

    public ShoppingCartStatus Status { get; private set; }

    public IList<PricedProductItem> ProductItems { get; private set; } = default!;

    public decimal TotalPrice => ProductItems.Sum(pi => pi.TotalPrice);

    public static ShoppingCart Open(
        Guid cartId,
        Guid clientId)
    {
        return new ShoppingCart(cartId, clientId);
    }

    public ShoppingCart(){}

    private ShoppingCart(
        Guid id,
        Guid clientId)
    {
        var @event = ShoppingCartOpened.Create(
            id,
            clientId
        );

        Apply(@event);
    }

    public void Apply(ShoppingCartOpened @event)
    {
        Id = @event.CartId;
        ClientId = @event.ClientId;
        ProductItems = new List<PricedProductItem>();
        Status = ShoppingCartStatus.Pending;
    }

    public void AddProduct(
        IProductPriceCalculator productPriceCalculator,
        ProductItem productItem)
    {
        if(Status != ShoppingCartStatus.Pending)
            throw new InvalidOperationException($"Adding product for the cart in '{Status}' status is not allowed.");

        var pricedProductItem = productPriceCalculator.Calculate(productItem).Single();

        var @event = ProductAdded.Create(Id, pricedProductItem);

        Apply(@event);
    }

    public void Apply(ProductAdded @event)
    {
        var newProductItem = @event.ProductItem;

        var existingProductItem = FindProductItemMatchingWith(newProductItem);

        if (existingProductItem is null)
        {
            ProductItems.Add(newProductItem);
            return;
        }

        ProductItems.Replace(
            existingProductItem,
            existingProductItem.MergeWith(newProductItem)
        );
    }

    public void RemoveProduct(
        PricedProductItem productItemToBeRemoved)
    {
        if(Status != ShoppingCartStatus.Pending)
            throw new InvalidOperationException($"Removing product from the cart in '{Status}' status is not allowed.");

        var existingProductItem = FindProductItemMatchingWith(productItemToBeRemoved);

        if (existingProductItem is null)
            throw new InvalidOperationException($"Product with id `{productItemToBeRemoved.ProductId}` and price '{productItemToBeRemoved.UnitPrice}' was not found in cart.");

        if(!existingProductItem.HasEnough(productItemToBeRemoved.Quantity))
            throw new InvalidOperationException($"Cannot remove {productItemToBeRemoved.Quantity} items of Product with id `{productItemToBeRemoved.ProductId}` as there are only ${existingProductItem.Quantity} items in card");

        var @event = ProductRemoved.Create(Id, productItemToBeRemoved);

        Apply(@event);
    }

    public void Apply(ProductRemoved @event)
    {
        var productItemToBeRemoved = @event.ProductItem;

        var existingProductItem = FindProductItemMatchingWith(@event.ProductItem);

        if (existingProductItem == null)
            return;

        if (existingProductItem.HasTheSameQuantity(productItemToBeRemoved))
        {
            ProductItems.Remove(existingProductItem);
            return;
        }

        ProductItems.Replace(
            existingProductItem,
            existingProductItem.Subtract(productItemToBeRemoved)
        );
    }

    public void Confirm()
    {
        if(Status != ShoppingCartStatus.Pending)
            throw new InvalidOperationException($"Confirming cart in '{Status}' status is not allowed.");

        if (ProductItems.Count == 0)
            throw new InvalidOperationException($"Confirming empty cart is not allowed.");

        var @event = ShoppingCartConfirmed.Create(Id, DateTime.UtcNow);

        Apply(@event);
    }

    public void Apply(ShoppingCartConfirmed @event)
    {
        Status = ShoppingCartStatus.Confirmed;
    }

    public void Cancel()
    {
        if(Status != ShoppingCartStatus.Pending)
            throw new InvalidOperationException($"Canceling cart in '{Status}' status is not allowed.");

        var @event = ShoppingCartCanceled.Create(Id, DateTime.UtcNow);

        Apply(@event);
    }

    public void Apply(ShoppingCartCanceled @event)
    {
        Status = ShoppingCartStatus.Canceled;
    }

    private PricedProductItem? FindProductItemMatchingWith(PricedProductItem productItem)
    {
        return ProductItems
            .SingleOrDefault(pi => pi.MatchesProductAndPrice(productItem));
    }
}



public class UnitTest1
{
    [Fact]
    public void Test1()
    {
    }
}
