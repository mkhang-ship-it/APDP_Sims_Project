namespace SIMS.Application.Validation;

public interface IValidator<in T>
{
    ValidationResult Validate(T input);
}
