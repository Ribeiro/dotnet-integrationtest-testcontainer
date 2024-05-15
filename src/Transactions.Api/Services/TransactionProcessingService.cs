using Microsoft.AspNetCore.Components.Web;

namespace Transactions.Api.Services;

internal sealed class TransactionProcessingService(
    ICustomerAccountServiceClient accountServiceClient,
    ITransactionRepository transactionRepository,
    IMessagePublisher publisher)
    : ITransactionProcessingService
{
    public async Task<IResult> ExecuteAsync(
        TransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validationResults = request.Validate(new ValidationContext(request));
            if (validationResults.Any())
            {
               return GetValidationProblem(validationResults, request);
            }

            var account = await accountServiceClient.FetchByIdAsync(request.AccountId, cancellationToken);
            if (account is null)
            {
                return GetUnprocessableEntity($"The account with id {request.AccountId} does not exist.",
                                              $"urn:transactions:register:{request.TransactionId}",
                                              "The account does not exist.");
            }

            if (!account.IsBalanceEnough(request.Amount))
            {
                return GetUnprocessableEntity($"The account with id {request.AccountId} does not have enough balance.",
                                              $"urn:transactions:register:{request.TransactionId}",
                                              "The account does not have enough balance.");
            }

            var transaction = new Transaction()
            {
                Id = request.TransactionId,
                AccountId = account.Id,
                Account = account,
                Amount = request.Amount,
                Currency = request.Currency,
                Date = request.TransactionDate,
            };

            var registeredTransaction = await transactionRepository.AddAsync(transaction, cancellationToken);

            await publisher.PublishAsync(registeredTransaction!, cancellationToken);

            return Results.Created(uri: $"/api/transactions/{registeredTransaction!.Id}",
                                   value: null);
        }
        catch (Exception ex)
        {
            return GetProblem(ex, request);
        }
    }

    private IResult GetValidationProblem(IEnumerable<ValidationResult>? validationResults, TransactionRequest request)
    {
        return Results.ValidationProblem(
                    errors: validationResults!.ToDictionary(
                        keySelector: validationResult => validationResult.MemberNames.First(),
                        elementSelector: validationResult => new[] { validationResult.ErrorMessage! }),
                    detail: "The request payload contains invalid data.",
                    instance: $"urn:transactions:register:{request.TransactionId}",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The request is invalid.",
                    type: "https://httpstatuses.com/400"
                );
    }

    private IResult GetUnprocessableEntity(string detail, string instance, string title)
    {
        return Results.UnprocessableEntity(new ProblemDetails
        {
            Detail = detail,
            Instance = instance,
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = title,
            Type = "https://httpstatuses.com/422"
        });
    }

       private IResult GetProblem(Exception ex, TransactionRequest request)
    {
         return Results.Problem(
                detail: ex.Message,
                instance: $"urn:transactions:register:{request.TransactionId}",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An error occurred while registering the transaction.",
                type: "https://httpstatuses.com/500");   
    }
}
