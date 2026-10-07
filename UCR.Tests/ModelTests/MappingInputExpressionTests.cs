using System.Collections.Generic;
using System.Linq;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Plugins.Remapper;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    public class MappingInputExpressionTests
    {
        [Test]
        public void InputExpression_UsesAndWithinGroups_OrBetweenGroups_AndSupportsNot()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding { InputExpressionGroup = 0 },
                    new DeviceBinding { InputExpressionGroup = 0 },
                    new DeviceBinding { InputExpressionGroup = 1 },
                    new DeviceBinding { InputExpressionGroup = 1, InputExpressionNegated = true }
                }
            };

            Assert.AreEqual(1, mapping.EvaluateInputExpression(new short[] { 1, 1, 0, 1 }),
                "Both positive terms in the first AND group should satisfy the expression.");
            Assert.AreEqual(1, mapping.EvaluateInputExpression(new short[] { 1, 0, 1, 0 }),
                "The second OR group should satisfy when its positive term is down and its NOT term is up.");
            Assert.AreEqual(0, mapping.EvaluateInputExpression(new short[] { 1, 0, 1, 1 }),
                "A pressed negated term must make its AND group false.");
            Assert.AreEqual(0, mapping.EvaluateInputExpression(new short[] { 0, 0, 0, 0 }),
                "No OR group is satisfied when all positive terms are up.");
        }

        [Test]
        public void ConditionApiKeepsAndTermsOnOneRowAndCreatesSeparateOrRows()
        {
            var mapping = new Mapping();
            mapping.Plugins.Add(new ButtonToButton());
            mapping.DeviceBindings.Add(new DeviceBinding());

            var secondAndTerm = mapping.AddExpressionInputToGroup(0);
            var secondCondition = mapping.AddExpressionCondition();
            var thirdAndTerm = mapping.AddExpressionInputToGroup(secondCondition.InputExpressionGroup);

            Assert.That(mapping.UseInputExpression, Is.True);
            Assert.That(mapping.DeviceBindings.Count, Is.EqualTo(4));
            Assert.That(mapping.DeviceBindings.Count(binding => binding.InputExpressionGroup == 0), Is.EqualTo(2));
            Assert.That(mapping.DeviceBindings.Count(binding => binding.InputExpressionGroup == 1), Is.EqualTo(2));
            Assert.That(secondAndTerm.InputExpressionGroup, Is.EqualTo(0));
            Assert.That(thirdAndTerm.InputExpressionGroup, Is.EqualTo(1));
        }

        [Test]
        public void RemovingAConditionCompactsRemainingGroupNumbers()
        {
            var mapping = new Mapping();
            mapping.Plugins.Add(new ButtonToButton());
            mapping.DeviceBindings.Add(new DeviceBinding());
            mapping.AddExpressionCondition();
            mapping.AddExpressionCondition();

            Assert.That(mapping.RemoveExpressionGroup(1), Is.True);
            Assert.That(mapping.DeviceBindings.Select(binding => binding.InputExpressionGroup).Distinct(),
                Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void InputExpression_DoesNotTreatIndividualChordMembersAsSatisfied()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding { InputExpressionGroup = 0 },
                    new DeviceBinding { InputExpressionGroup = 0 }
                }
            };

            Assert.AreEqual(0, mapping.EvaluateInputExpression(new short[] { 1, 0 }));
            Assert.AreEqual(0, mapping.EvaluateInputExpression(new short[] { 0, 1 }));
            Assert.AreEqual(1, mapping.EvaluateInputExpression(new short[] { 1, 1 }));
        }
    }
}
