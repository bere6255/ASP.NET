using FluentValidation;
using PaymentsApi.Models.DTOs;

namespace PaymentsApi.Validators;

public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(request => request.FromAccountId).NotEmpty();
        RuleFor(request => request.ToAccountId).NotEmpty();
        RuleFor(request => request.ToAccountId).NotEqual(request => request.FromAccountId);
        RuleFor(request => request.Amount).GreaterThan(0).PrecisionScale(18, 2, false);
    }
}
