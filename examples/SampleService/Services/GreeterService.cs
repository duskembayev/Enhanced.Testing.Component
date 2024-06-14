using Grpc.Core;
using SampleService.Models;

namespace SampleService.Services;

public class GreeterService(PeopleDbContext dbContext) : Greeter.GreeterBase
{
    public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        await dbContext.Persons.AddAsync(new Person { Name = request.Name }, context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        return new HelloReply { Message = "Hello " + request.Name };
    }
}
