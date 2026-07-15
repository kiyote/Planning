using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Model.Tests;

public class PlanTests {

	[Test]
	public void Ctor_ValidPlan_ThrowsNoException() {
		Assert.That(
			() => TestPlanFactory.Create(),
			Throws.Nothing
		);
	}
}
