using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class AssetCompiler {

	public IEnumerable<CompiledAsset> Compile(
		Plan plan,
		IEnumerable<CompiledMember> members
	) {
		List<CompiledAsset> result = [];
		foreach( Asset asset in plan.Assets ) {
			CompiledMember member = members.Single( m => m.Name == asset.Member );

			result.Add(
				new CompiledAsset(
					AssetId: new( result.Count + 1 ),
					Name: asset.Name,
					TaxStatus: asset.TaxStatus,
					MemberId: member.MemberId,
					Amount: asset.Amount,
					ContributionBacklog: asset.ContributionBacklog,
					AnnualContributionLimit: asset.AnnualContributionLimit,
					HasUnlimitedContributionRoom: asset.HasUnlimitedContributionRoom,
					CostBase: asset.CostBase
				)
			);
		}

		return result;
	}
}
