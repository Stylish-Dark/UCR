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
        public void FourWayJoystick_OppositeDirectionsCancel()
        {
            Assert.That(ButtonsToAxes.DirectionValue(false, false), Is.EqualTo(0));
            Assert.That(ButtonsToAxes.DirectionValue(true, true), Is.EqualTo(0));
            Assert.That(ButtonsToAxes.DirectionValue(true, false), Is.EqualTo(HidWizards.UCR.Core.Utilities.Constants.AxisMinValue));
            Assert.That(ButtonsToAxes.DirectionValue(false, true), Is.EqualTo(HidWizards.UCR.Core.Utilities.Constants.AxisMaxValue));
        }

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
        public void MixedButtonAndDirectedAxes_UseIndividualThresholdsAndNegation()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding { InputExpressionGroup = 0, InputExpressionInputKind = 0 },
                    new DeviceBinding { InputExpressionGroup = 0, InputExpressionInputKind = 1, InputExpressionAxisThreshold = 12000 },
                    new DeviceBinding { InputExpressionGroup = 0, InputExpressionInputKind = 2, InputExpressionAxisThreshold = 20000, InputExpressionNegated = true },
                    new DeviceBinding { InputExpressionGroup = 1, InputExpressionInputKind = 2, InputExpressionAxisThreshold = 16000 }
                }
            };
            Assert.AreEqual(1, mapping.EvaluateInputExpression(new short[] { 1, 14000, 0, 0 }),
                "A held button and right-axis threshold can satisfy one group when the negated left-axis term is not met.");
            Assert.AreEqual(0, mapping.EvaluateInputExpression(new short[] { 1, 11999, 0, 0 }),
                "An axis below the positive threshold must not count as active.");
            Assert.AreEqual(0, mapping.EvaluateInputExpression(new short[] { 1, 14000, -22000, 0 }),
                "The negated left-axis term must suppress its AND group.");
            Assert.AreEqual(1, mapping.EvaluateInputExpression(new short[] { 0, 0, 0, -17000 }),
                "A negative-direction axis can independently satisfy an alternative OR group.");
            Assert.AreEqual(0, mapping.EvaluateInputExpression(new short[] { 0, -28000, 0, -15000 }),
                "Wrong axis direction and subthreshold values must not activate the expression.");
        }

        [Test]
        public void ContinuousAxisSignal_PreservesFullAnalogueValueUnlessChordGateIsUnsatisfied()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding { InputExpressionInputKind = 3, InputExpressionGroup = 0 },
                    new DeviceBinding { InputExpressionInputKind = 0, InputExpressionGroup = 0 },
                    new DeviceBinding { InputExpressionInputKind = 1, InputExpressionAxisThreshold = 10000,
                        InputExpressionNegated = true, InputExpressionGroup = 0 }
                }
            };
            Assert.AreEqual(0, mapping.EvaluateAnalogueInputExpression(new short[] { -23345, 0, 0 }));
            Assert.AreEqual(-23345, mapping.EvaluateAnalogueInputExpression(new short[] { -23345, 1, 0 }));
            Assert.AreEqual(0, mapping.EvaluateAnalogueInputExpression(new short[] { -23345, 1, 12000 }));
            Assert.AreEqual(14444, mapping.EvaluateAnalogueInputExpression(new short[] { 14444, 1, 9000 }));
        }

        [Test]
        public void DigitalChord_DrivesAnAnalogueOutputAtFullScale()
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
            Assert.AreEqual(0, mapping.EvaluateAnalogueInputExpression(new short[] { 1, 0 }));
            Assert.AreEqual(short.MaxValue, mapping.EvaluateAnalogueInputExpression(new short[] { 1, 1 }));
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
