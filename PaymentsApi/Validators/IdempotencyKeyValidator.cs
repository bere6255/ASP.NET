using FluentValidation;

namespace PaymentsApi.Validators;

public sealed class IdempotencyKeyValidator : AbstractValidator<string>
{
    public IdempotencyKeyValidator()
    {
        RuleFor(key => key).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
