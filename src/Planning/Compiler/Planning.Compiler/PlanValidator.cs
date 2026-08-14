using Planning.Model;
using Planning.Model.Plans;

namespace Planning.Compiler;

public sealed class PlanValidator {

	private const int MinimumCPPStartAge = 60;
	private const int MaximumCPPStartAge = 70;
	private const int RequiredHouseholdSize = 2;

	public PlanValidationResult Validate(
		Plan plan
	) {
		PlanValidationResult result = new PlanValidationResult();

		Member[] members = [.. plan.Members];

		ValidateMembers( plan, members, result );
		ValidatePlanAssumptions( plan, result );
		ValidateAssets( plan, members, result );
		ValidateLifeInsurance( plan, members, result );
		ValidateContributions( plan, members, result );
		ValidateBurndown( plan, result );

		return result;
	}

	private static void ValidateMembers(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
		if( members.Length == 0 ) {
			result.AddError( "The plan must contain at least one member." );
			return;
		}

		if( members.Length != RequiredHouseholdSize ) {
			result.AddError( $"The plan must contain exactly {RequiredHouseholdSize} members but contains {members.Length}." );
		}

		foreach( string duplicateName in members
			.GroupBy( m => m.Name )
			.Where( g => g.Count() > 1 )
			.Select( g => g.Key ) ) {
			result.AddError( $"Member name '{duplicateName}' is used more than once; member names must be unique." );
		}

		foreach( Member member in members ) {
			if( string.IsNullOrWhiteSpace( member.Name ) ) {
				result.AddError( "A member has an empty name." );
			}

			if( member.TargetAgeInYears <= 0 ) {
				result.AddError( $"Member '{member.Name}' must have a positive target age but has {member.TargetAgeInYears}." );
			}

			if( member.BirthDate == default ) {
				result.AddError( $"Member '{member.Name}' must have a valid birth date." );
			} else if( member.BirthDate >= plan.StartDate ) {
				result.AddError( $"Member '{member.Name}' birth date ({member.BirthDate:yyyy-MM-dd}) must be before the plan start date ({plan.StartDate:yyyy-MM-dd})." );
			}

			if( member.RetirementAgeInYears.HasValue
				&& member.RetirementAgeInYears.Value >= member.TargetAgeInYears ) {
				result.AddError( $"Member '{member.Name}' retirement age ({member.RetirementAgeInYears}) must be before the target age ({member.TargetAgeInYears})." );
			}

			if( member.CPPStartInYears < MinimumCPPStartAge
				|| member.CPPStartInYears > MaximumCPPStartAge ) {
				result.AddError( $"Member '{member.Name}' CPP start age ({member.CPPStartInYears}) must be between {MinimumCPPStartAge} and {MaximumCPPStartAge} inclusive." );
			}

			if( member.CPPPercent < 0m || member.CPPPercent > 100m ) {
				result.AddError( $"Member '{member.Name}' CPP percentage ({member.CPPPercent}) must be between 0 and 100 inclusive." );
			}
		}

		if( members.Length > 0 && !members.Any( m => m.RetirementAgeInYears.HasValue ) ) {
			result.AddError( "At least one household member must specify a retirement age." );
		}
	}

	private static void ValidatePlanAssumptions(
		Plan plan,
		PlanValidationResult result
	) {
		if( plan.StartDate == default ) {
			result.AddError( "The plan must have a valid start date." );
		}

		if( plan.CPPMaximum < 0m ) {
			result.AddError( "CPP maximum must be nonnegative." );
		}

		if( plan.CPPCombinedSurvivorMaximum < 0m ) {
			result.AddError( "CPP combined survivor maximum must be nonnegative." );
		}

		if( plan.OASMaximum < 0m ) {
			result.AddError( "OAS maximum must be nonnegative." );
		}
	}

	private static void ValidateAssets(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
		HashSet<string> memberNames = [.. members.Select( m => m.Name )];

		foreach( Asset asset in plan.Assets ) {
			if( !memberNames.Contains( asset.Member ) ) {
				result.AddError( $"Asset '{asset.Name}' references unknown member '{asset.Member}'." );
			}

			if( asset.Amount < 0m ) {
				result.AddError( $"Asset '{asset.Name}' amount ({asset.Amount}) must be nonnegative." );
			}

			ValidateOrderedRangedValues( asset, result );
		}

		foreach( var duplicate in plan.Assets
			.GroupBy( a => ( a.Member, a.Name ) )
			.Where( g => g.Count() > 1 ) ) {
			result.AddError( $"Member '{duplicate.Key.Member}' has more than one asset named '{duplicate.Key.Name}'; assets must be unique within a member's scope." );
		}

		ValidateAssetTaxStatusCoverage( plan, members, result );
	}

	/// <summary>
	/// Asserts that every member holds an account of each tax status. <see cref="Plan"/> synthesizes
	/// any account a plan does not define, so this can only fail if that normalization is bypassed or
	/// removed. Downstream code relies on the invariant - most notably the rollover to a surviving
	/// spouse, which needs a destination of matching tax status - so the plan is rejected rather than
	/// allowed to proceed with balances that would land in an account of the wrong tax treatment.
	/// </summary>
	private static void ValidateAssetTaxStatusCoverage(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
		foreach( Member member in members ) {
			HashSet<AssetTaxStatus> memberStatuses = [
				.. plan.Assets.Where( a => a.Member == member.Name ).Select( a => a.TaxStatus )
			];

			foreach( AssetTaxStatus status in Enum.GetValues<AssetTaxStatus>() ) {
				if( !memberStatuses.Contains( status ) ) {
					result.AddError( $"Member '{member.Name}' must have a {status} asset defined, even if its amount is 0." );
				}
			}
		}
	}

	/// <summary>
	/// The burndown strategy is optional, but when configured it needs a positive year count. The
	/// taxable account it draws down and the capital-gains account that receives the proceeds are
	/// guaranteed to exist by the tax status coverage invariant.
	/// </summary>
	private static void ValidateBurndown(
		Plan plan,
		PlanValidationResult result
	) {
		if( plan.Burndown is null ) {
			return;
		}

		if( plan.Burndown.BurndownYears <= 0 ) {
			result.AddError( $"Burndown years ({plan.Burndown.BurndownYears}) must be positive." );
		}
	}

	private static void ValidateOrderedRangedValues(
		Asset asset,
		PlanValidationResult result
	) {
		RangedValue[] returns = [.. asset.ReturnPercentages];
		for( int i = 1; i < returns.Length; i++ ) {
			if( returns[i].StartDate < returns[i - 1].StartDate ) {
				result.AddError( $"Asset '{asset.Name}' return percentages must be ordered by start date." );
				break;
			}
		}
	}

	private static void ValidateLifeInsurance(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
		HashSet<string> memberNames = [.. members.Select( m => m.Name )];

		foreach( LifeInsurance insurance in plan.LifeInsurance ) {
			if( !memberNames.Contains( insurance.MemberName ) ) {
				result.AddError( $"Life insurance references unknown member '{insurance.MemberName}'." );
			}

			if( insurance.Amount < 0m ) {
				result.AddError( $"Life insurance for '{insurance.MemberName}' amount ({insurance.Amount}) must be nonnegative." );
			}
		}
	}

	private static void ValidateContributions(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
		HashSet<string> memberNames = [.. members.Select( m => m.Name )];

		foreach( Contribution contribution in plan.Contributions ) {
			if( !memberNames.Contains( contribution.Member ) ) {
				result.AddError( $"Contribution references unknown member '{contribution.Member}'." );
			}

			if( contribution.Amount < 0m ) {
				result.AddError( $"Contribution for '{contribution.Member}' amount ({contribution.Amount}) must be nonnegative." );
			}

			if( contribution.StartYear <= 0 ) {
				result.AddError( $"Contribution for '{contribution.Member}' start year ({contribution.StartYear}) must be a valid year." );
			}
		}
	}
}
