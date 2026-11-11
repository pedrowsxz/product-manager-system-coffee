using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Moinho.Web.Models;

namespace Moinho.Web.ViewModels;

public class ProductListQuery
{
    public const int PageSize = 20;

    public string? Q { get; set; }
    public string? Status { get; set; }        // "active", "inactive" or empty for all
    public string Sort { get; set; } = "name"; // "name", "price" or "date"
    public string Dir { get; set; } = "asc";   // "asc" or "desc"
    public int Page { get; set; } = 1;
}

// What the create/edit forms post. It deliberately has no Id, DateAdded or UpdatedAt, so those
// can't be over-posted: the server sets them.
public class ProductFormViewModel : IValidatableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // pt-BR money as typed by a person: "12", "12,5", "12,50", "1.234,56" or "1234,56".
    // "12.50" is rejected: in pt-BR the dot groups thousands, so it would silently become 1250.
    public const string PricePattern = @"^\d{1,3}(\.\d{3})*(,\d{1,2})?$|^\d+(,\d{1,2})?$";

    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre {2} e {1} caracteres.")]
    [Display(Name = "Nome")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Informe o SKU.")]
    [RegularExpression("^[A-Za-z0-9-]{3,20}$", ErrorMessage = "Use de 3 a 20 letras, números ou hífen.")]
    [Display(Name = "SKU")]
    public string Sku { get; set; } = "";

    [StringLength(1000, ErrorMessage = "A descrição pode ter no máximo {1} caracteres.")]
    [Display(Name = "Descrição")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Informe o preço.")]
    [RegularExpression(PricePattern, ErrorMessage = "Use o formato brasileiro, com vírgula nos centavos: 12,50 ou 1.234,56.")]
    [Display(Name = "Preço (R$)")]
    public string Price { get; set; } = "";

    [Range(0, int.MaxValue, ErrorMessage = "O estoque não pode ser negativo.")]
    [Display(Name = "Estoque")]
    public int StockQuantity { get; set; }

    [Display(Name = "Ativo")]
    public bool IsActive { get; set; } = true;

    // A method, not a property: MVC validation reads every property, even before the format is checked.
    public decimal ParsePrice() => decimal.Parse(Price, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, PtBr);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // MVC calls this even when the attributes above failed, so only check a well-formed price.
        if (!Regex.IsMatch(Price ?? "", PricePattern))
        {
            yield break;
        }
        if (ParsePrice() <= 0)
        {
            yield return new ValidationResult("O preço deve ser maior que zero.", [nameof(Price)]);
        }
        else if (ParsePrice() > 99_999_999.99m) // decimal(10,2)
        {
            yield return new ValidationResult("Preço alto demais.", [nameof(Price)]);
        }
    }

    public static ProductFormViewModel From(Product product) => new()
    {
        Name = product.Name,
        Sku = product.Sku,
        Description = product.Description,
        Price = product.Price.ToString("0.00", PtBr),
        StockQuantity = product.StockQuantity,
        IsActive = product.IsActive,
    };

    public void ApplyTo(Product product)
    {
        product.Name = Name.Trim();
        product.Sku = Sku.Trim().ToUpperInvariant();
        product.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        product.Price = ParsePrice();
        product.StockQuantity = StockQuantity;
        product.IsActive = IsActive;
    }
}
