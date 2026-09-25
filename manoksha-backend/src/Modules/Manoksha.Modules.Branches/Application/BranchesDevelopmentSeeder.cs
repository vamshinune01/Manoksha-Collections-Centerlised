using Manoksha.Application.Modules;
using Manoksha.Modules.Branches.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Branches.Application;

/// <summary>LOCAL/DEVELOPMENT ONLY: Karimnagar (P1), Hyderabad (P2), Mulugu (P3) with well-known ids.</summary>
internal sealed class BranchesDevelopmentSeeder(ManokshaDbContext db, BranchService branches) : IDevelopmentSeeder
{
    public int Order => 5;

    public async Task SeedAsync(DevelopmentSeedContext context, CancellationToken cancellationToken)
    {
        (Guid Id, string Code, string Name)[] seeds =
        [
            (DevelopmentSeedData.BranchKarimnagar, "KNR", "Karimnagar"),
            (DevelopmentSeedData.BranchHyderabad, "HYD", "Hyderabad"),
            (DevelopmentSeedData.BranchMulugu, "MLG", "Mulugu"),
        ];
        foreach (var s in seeds)
        {
            if (!await db.Set<Branch>().AnyAsync(b => b.Id == s.Id, cancellationToken))
            {
                await branches.CreateAsync(s.Id, new CreateBranchRequest(s.Code, s.Name, new BranchAddressDto(null, s.Name, "Telangana", null, null), "Development seed"), cancellationToken);
            }
        }
    }
}
