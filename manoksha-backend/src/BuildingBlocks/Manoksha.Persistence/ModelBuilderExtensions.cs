using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Manoksha.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>Removes the default string max length (used for jsonb and long text columns).</summary>
    public static PropertyBuilder<T> Unbounded<T>(this PropertyBuilder<T> builder)
    {
        builder.Metadata.SetMaxLength(null);
        return builder;
    }
}
