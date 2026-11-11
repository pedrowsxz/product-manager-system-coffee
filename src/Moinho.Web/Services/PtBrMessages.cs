using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Moinho.Web.Services;

// pt-BR texts for the model-binding errors MVC and Razor Pages produce (e.g. "abc" typed in a number field).
public static class PtBrMessages
{
    public static void Apply(DefaultModelBindingMessageProvider messages)
    {
        messages.SetValueMustNotBeNullAccessor(field => $"Preencha o campo {field}.");
        messages.SetMissingBindRequiredValueAccessor(field => $"Preencha o campo {field}.");
        messages.SetMissingKeyOrValueAccessor(() => "Valor obrigatório.");
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "O corpo da requisição é obrigatório.");
        messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"O valor '{value}' não é válido para {field}.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"O valor '{value}' não é válido.");
        messages.SetUnknownValueIsInvalidAccessor(field => $"O valor informado não é válido para {field}.");
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "O valor informado não é válido.");
        messages.SetValueIsInvalidAccessor(value => $"O valor '{value}' não é válido.");
        messages.SetValueMustBeANumberAccessor(field => $"O campo {field} deve ser um número.");
        messages.SetNonPropertyValueMustBeANumberAccessor(() => "O valor deve ser um número.");
    }
}
