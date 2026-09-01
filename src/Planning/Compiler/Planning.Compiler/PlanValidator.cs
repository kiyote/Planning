using Planning.Model.Plans;

namespace Planning.Compiler;

public sealed class PlanValidator {

	private const int MinimumCPPStartAge = 60;
	private const int MaximumCPPStartAge = 70;
	private const int RequiredHouseholdSize = 2;
	private const int MaximumTaxPolicyAgeInYears = 5;

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
		ValidateInheritance( plan, members, result );
		ValidateBurndown( plan, result );
		ValidateTaxPolicy( plan, result );

		return result;
	}

	private static void ValidateMembers(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
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

			if( asset.CostBase < 0m ) {
				result.AddError( $"Asset '{asset.Name}' cost base ({asset.CostBase}) must be nonnegative." );
			}

			// A cost base only describes how much of a balance escapes capital-gains tax. Any
			// other tax status either taxes the whole withdrawal as income or taxes none of it,
			// so a non-zero value there is a mistake rather than a preference.
			if( asset.TaxStatus != AssetTaxStatus.CapitalGains && asset.CostBase != 0m ) {
				result.AddError( $"Asset '{asset.Name}' has a cost base ({asset.CostBase}) but its tax status is {asset.TaxStatus}; only CapitalGains assets can carry a cost base." );
			}

			// A cost base above the balance is an unrealized loss. Gains are floored at zero and
			// there is no loss-carryforward concept, so accepting it would silently discard it.
			if( asset.TaxStatus == AssetTaxStatus.CapitalGains && asset.CostBase > asset.Amount ) {
				result.AddError( $"Asset '{asset.Name}' cost base ({asset.CostBase}) exceeds its amount ({asset.Amount}); unrealized losses are not modelled." );
			}
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

			foreach( AssetTaxStatus status in Enum.GetValues<AssetTaxStatus>().Where( a => a > 0 ) ) {
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
		if( plan.Burndown.BurndownYears < 0 ) {
			result.AddError( $"Burndown years ({plan.Burndown.BurndownYears}) must be positive." );
		}
	}

	/// <summary>
	/// The tax policy's monetary values are expressed in the dollars of its own year, and the
	/// calculator indexes them forward by inflation to each year it projects. That only makes
	/// sense for a policy whose year is at or before the plan start: a future-dated policy would
	/// be indexed by a negative exponent and silently deflate its brackets. A policy more than
	/// <see cref="MaximumTaxPolicyAgeInYears"/> years stale is rejected because compounding
	/// assumed inflation that far forward stops being a credible stand-in for the real figures.
	/// </summary>
	private static void ValidateTaxPolicy(
		Plan plan,
		PlanValidationResult result
	) {
		int planStartYear = plan.StartDate.Year;
		int policyYear = plan.TaxPolicy.Year;

		if( policyYear > planStartYear ) {
			result.AddError( $"Tax policy year ({policyYear}) must not be after the plan start year ({planStartYear})." );
		} else if( policyYear < planStartYear - MaximumTaxPolicyAgeInYears ) {
			result.AddError( $"Tax policy year ({policyYear}) must not be more than {MaximumTaxPolicyAgeInYears} years before the plan start year ({planStartYear})." );
		}
	}

	private static void ValidateLifeInsurance(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
		HashSet<string> memberNames = [.. members.Select( m => m.Name )];

		foreach( LifeInsurance insurance in plan.LifeInsurance ) {
			if( !memberNames.Contains( insurance.Member ) ) {
				result.AddError( $"Life insurance references unknown member '{insurance.Member}'." );
			}

			if( insurance.Amount < 0m ) {
				result.AddError( $"Life insurance for '{insurance.Member}' amount ({insurance.Amount}) must be nonnegative." );
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

			// A spousal contribution must name a real, different member to fund it. Naming the
			// annuitant themselves is simply an ordinary contribution and needs no error.
			if( !string.IsNullOrWhiteSpace( contribution.Spousal )
				&& !memberNames.Contains( contribution.Spousal )
			) {
				result.AddError( $"Contribution for '{contribution.Member}' references unknown spousal contributor '{contribution.Spousal}'." );
			}
		}
	}

	private static void ValidateInheritance(
		Plan plan,
		Member[] members,
		PlanValidationResult result
	) {
		Dictionary<string, Member> membersByName = members
			.GroupBy( m => m.Name )
			.ToDictionary( g => g.Key, g => g.First() );

		foreach( Inheritance inheritance in plan.Inheritance ) {
			if( !membersByName.TryGetValue( inheritance.Member, out Member? member ) ) {
				result.AddError( $"Inheritance references unknown member '{inheritance.Member}'." );
				continue;
			}

			if( inheritance.Amount < 0m ) {
				result.AddError( $"Inheritance for '{inheritance.Member}' amount ({inheritance.Amount}) must be nonnegative." );
			}

			if( inheritance.AgeReceived < 0 ) {
				result.AddError( $"Inheritance for '{inheritance.Member}' age received ({inheritance.AgeReceived}) must be nonnegative." );
			} else if( inheritance.AgeReceived > member.TargetAgeInYears ) {
				result.AddError( $"Inheritance for '{inheritance.Member}' age received ({inheritance.AgeReceived}) must not exceed the member's target age ({member.TargetAgeInYears})." );
			}
		}
	}
}
