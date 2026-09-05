using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Json;

namespace recepcaototem.Features.Common;

public interface IStrictModuleRequest;

public static class StrictBody
{
    public static void Configure(JsonOptions options)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeof(IStrictModuleRequest).IsAssignableFrom(typeInfo.Type))
                typeInfo.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, resolver);
    }
}
