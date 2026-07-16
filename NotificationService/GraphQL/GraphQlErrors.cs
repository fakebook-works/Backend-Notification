using HotChocolate;

namespace NotificationService.GraphQL;

internal static class GraphQlErrors
{
    public static GraphQLException BadUserInput(string message)
        => new(
            ErrorBuilder.New()
                .SetMessage(message)
                .SetCode("BAD_USER_INPUT")
                .Build());
}
