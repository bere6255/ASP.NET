using FluentValidation;
using PaymentsApi.Models.DTOs;

namespace PaymentsApi.Validators;

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(request => request.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
