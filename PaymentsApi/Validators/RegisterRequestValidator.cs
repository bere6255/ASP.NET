using FluentValidation;
using PaymentsApi.Models.DTOs;

namespace PaymentsApi.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(request => request.Password).NotEmpty().MinimumLength(12)
            .Matches("[A-Z]").WithMessage("must include an uppercase letter")
            .Matches("[a-z]").WithMessage("must include a lowercase letter")
            .Matches("[0-9]").WithMessage("must include a number")
            .Matches("[^a-zA-Z0-9]").WithMessage("must include a symbol");
    }
}
