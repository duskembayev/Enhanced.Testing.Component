using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Enhanced.Testing.Component.WireMock;

internal static class GrpcUnmatchedCalls
{
    private const int MaxRequestLength = 500;
    private const string Unimplemented = "12";

    public static void Explain(WireMockServer server)
    {
        server.Given(Request.Create().UsingPost().WithHeader("Content-Type", "application/grpc*"))
              .AtPriority(int.MaxValue)
              .RespondWith(Response.Create().WithCallback(request => Respond(server, request)));
    }

    private static ResponseMessage Respond(WireMockServer server, IRequestMessage request)
    {
        var response = new ResponseMessage { StatusCode = 200 };

        response.AddHeader("Content-Type", "application/grpc");
        response.AddHeader("grpc-status", Unimplemented);
        response.AddHeader("grpc-message", Uri.EscapeDataString(Describe(server, request)));

        return response;
    }

    private static string Describe(WireMockServer server, IRequestMessage request)
    {
        var method = request.Path.TrimStart('/');
        var stubs = server.Mappings
                          .Select(mapping => GrpcStub.Find(mapping.RequestMatcher))
                          .Where(stub => stub?.Method is not null)
                          .Select(stub => stub!)
                          .ToList();

        var ofMethod = stubs.Where(stub => stub.Method == method).ToList();

        if (ofMethod.Count > 0)
        {
            var message = ofMethod.Select(stub => stub.DescribeRequest?.Invoke(request.BodyAsBytes))
                                  .FirstOrDefault(description => description is not null);

            return $"No stub matched the gRPC call '{method}': the request did not satisfy the {ofMethod.Count} " +
                   "stub(s) of this method." + (message is null ? "" : $" Request: {Shorten(message)}");
        }

        var methods = stubs.Select(stub => stub.Method!).Distinct().Order().ToList();

        return methods.Count == 0
            ? $"No stub matched the gRPC call '{method}': the server has no gRPC stubs."
            : $"No stub matched the gRPC call '{method}': the method has no stubs. " +
              $"Stubbed methods: {string.Join(", ", methods)}.";
    }

    private static string Shorten(string text) =>
        text.Length <= MaxRequestLength ? text : text[..MaxRequestLength] + "...";
}
