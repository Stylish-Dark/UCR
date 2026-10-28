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
        public void LegacyMultiChannelSlots_AcceptIndependentlyTypedPhysicalInputs()
        {
            var rightAxis = new DeviceBinding { InputExpressionInputKind = 1, InputExpressionAxisThreshold = 14000 };
            var leftAxis = new DeviceBinding { InputExpressionInputKind = 2, InputExpressionAxisThreshold = 14000 };
            var rawAxis = new DeviceBinding { InputExpressionInputKind = 3 };
            var button = new DeviceBinding { InputExpressionInputKind = 0 };

            Assert.AreEqual(1, Mapping.AdaptPositionalInput(20000, rightAxis, DeviceBindingCategory.Momentary));
            Assert.AreEqual(0, Mapping.AdaptPositionalInput(-20000, rightAxis, DeviceBindingCategory.Momentary));
            Assert.AreEqual(short.MinValue, Mapping.AdaptPositionalInput(-20000, leftAxis, DeviceBindingCategory.Range));
            Assert.AreEqual(0, Mapping.AdaptPositionalInput(-10000, leftAxis, DeviceBindingCategory.Range));
            Assert.AreEqual(13200, Mapping.AdaptPositionalInput(13200, rawAxis, DeviceBindingCategory.Range));
            Assert.AreEqual(0, Mapping.AdaptPositionalInput(12000, rawAxis, DeviceBindingCategory.Momentary));
            Assert.AreEqual(1, Mapping.AdaptPositionalInput(20000, rawAxis, DeviceBindingCategory.Momentary));
            Assert.AreEqual(short.MaxValue, Mapping.AdaptPositionalInput(1, button, DeviceBindingCategory.Range));
            Assert.AreEqual(0, Mapping.AdaptPositionalInput(0, button, DeviceBindingCategory.Range));
        }

        [Test]
        public void DirectionalInputUnits_ConvertBetweenSinglePairAndFourWayWithoutLeakingSlots()
        {
            var mapping = new Mapping();
            mapping.Plugins.Add(new AxisToAxis());
            mapping.DeviceBindings.Add(new DeviceBinding());
            Assert.That(mapping.SetDirectionalInputMode(0, 2), Is.True);
            Assert.That(mapping.UseInputExpression, Is.True);
            Assert.That(mapping.DeviceBindings.Select(b => b.InputDirectionSlot), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(mapping.DeviceBindings.All(b => b.DeviceBindingCategory == DeviceBindingCategory.Momentary),
                Is.True);

            Assert.That(mapping.SetDirectionalInputMode(0, 4), Is.True);
            Assert.That(mapping.DeviceBindings.Select(b => b.InputDirectionSlot), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(mapping.SetDirectionalInputMode(0, 0), Is.True);
            Assert.That(mapping.DeviceBindings.Count, Is.EqualTo(1));
            Assert.That(mapping.DeviceBindings[0].InputDirectionSlot, Is.EqualTo(0));
            Assert.That(mapping.DeviceBindings[0].InputExpressionInputKind, Is.EqualTo(3),
                "Single-source mode must restore continuous analogue capture.");
        }

        [Test]
        public void DirectionalOutputSlots_ObserveEachDirectionAndRespectOrGroupOwnership()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DirectionalButtonOutputs = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding { InputDirectionSlot = 1, InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 2, InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 3, InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 4, InputExpressionGroup = 0 },
                    new DeviceBinding { InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 1, InputExpressionGroup = 1 },
                    new DeviceBinding { InputDirectionSlot = 2, InputExpressionGroup = 1 },
                    new DeviceBinding { InputDirectionSlot = 3, InputExpressionGroup = 1 },
                    new DeviceBinding { InputDirectionSlot = 4, InputExpressionGroup = 1 }
                }
            };
            var input = new short[] { 0, 0, 1, 0, 1, 0, 1, 0, 0 };
            Assert.IsTrue(mapping.EvaluateDirectionalButton(input, 3));
            Assert.IsFalse(mapping.EvaluateDirectionalButton(input, 2),
                "An active first group must not combine with a direction from another group.");
            input[4] = 0;
            Assert.IsTrue(mapping.EvaluateDirectionalButton(input, 2));
            Assert.IsFalse(mapping.EvaluateDirectionalButton(input, 3));
        }

        [Test]
        public void DirectionalOutputSlots_ApplyPerDirectionAxisThresholds()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding
                    {
                        InputDirectionSlot = 1,
                        InputExpressionInputKind = 2,
                        InputExpressionAxisThreshold = 13000
                    },
                    new DeviceBinding
                    {
                        InputDirectionSlot = 2,
                        InputExpressionInputKind = 1,
                        InputExpressionAxisThreshold = 18000
                    }
                }
            };
            Assert.IsFalse(mapping.EvaluateDirectionalButton(new short[] { -12000, 0 }, 1));
            Assert.IsTrue(mapping.EvaluateDirectionalButton(new short[] { -20000, 0 }, 1));
            Assert.IsFalse(mapping.EvaluateDirectionalButton(new short[] { -20000, 0 }, 2));
            Assert.IsTrue(mapping.EvaluateDirectionalButton(new short[] { 0, 20000 }, 2));
        }

        [Test]
        public void DirectionalLayoutResize_KeepsExistingBindingSlots()
        {
            var mapping = new Mapping();
            mapping.Plugins.Add(new AxisToAxis());
            var negative = new DeviceBinding();
            mapping.DeviceBindings.Add(negative);
            Assert.IsTrue(mapping.SetDirectionalInputMode(0, 2));
            var positive = mapping.DeviceBindings[1];
            negative.InputExpressionAxisThreshold = 19000;
            positive.InputExpressionAxisThreshold = 21000;
            Assert.IsTrue(mapping.SetDirectionalInputMode(0, 4));
            Assert.AreSame(negative, mapping.DeviceBindings[0]);
            Assert.AreSame(positive, mapping.DeviceBindings[1]);
            Assert.AreEqual(19000, mapping.DeviceBindings[0].InputExpressionAxisThreshold);
            Assert.AreEqual(21000, mapping.DeviceBindings[1].InputExpressionAxisThreshold);
            Assert.IsTrue(mapping.SetDirectionalInputMode(0, 4));
            Assert.AreSame(negative, mapping.DeviceBindings[0]);
            Assert.AreSame(positive, mapping.DeviceBindings[1]);
        }

        [Test]
        public void DirectionalJoystick_PairsOpposingKeysAndGatesBothAxesWithOtherAndInputs()
        {
            var bindings = Enumerable.Range(1, 4).Select(slot => new DeviceBinding
            {
                InputDirectionSlot = slot,
                InputExpressionInputKind = 0,
                InputExpressionGroup = 0
            }).ToList();
            bindings.Add(new DeviceBinding { InputExpressionGroup = 0 });
            var mapping = new Mapping { UseInputExpression = true, DeviceBindings = bindings };

            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 1, 0, 0, 0, 1 }, 0),
                Is.EqualTo(short.MinValue + 1));
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 0, 1, 0, 0, 1 }, 0),
                Is.EqualTo(short.MaxValue));
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 1, 1, 0, 0, 1 }, 0),
                Is.EqualTo(0), "Opposing directions must cancel.");
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 0, 0, 1, 0, 1 }, 1),
                Is.EqualTo(short.MinValue + 1));
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 0, 0, 0, 1, 1 }, 1),
                Is.EqualTo(short.MaxValue));
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 0, 1, 0, 1, 0 }, 0),
                Is.EqualTo(0), "A separate inactive AND condition must gate X.");
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 0, 1, 0, 1, 0 }, 1),
                Is.EqualTo(0), "The same AND condition must gate Y.");
        }

        [Test]
        public void JoystickOrGroups_NeverMergeXFromOneAlternativeWithYFromAnother()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding { InputDirectionSlot = 1, InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 2, InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 3, InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 4, InputExpressionGroup = 0 },
                    new DeviceBinding { InputDirectionSlot = 1, InputExpressionGroup = 1 },
                    new DeviceBinding { InputDirectionSlot = 2, InputExpressionGroup = 1 },
                    new DeviceBinding { InputDirectionSlot = 3, InputExpressionGroup = 1 },
                    new DeviceBinding { InputDirectionSlot = 4, InputExpressionGroup = 1 }
                }
            };
            var input = new short[] { 0, 0, 0, 1, 0, 1, 0, 0 };
            Assert.That(mapping.EvaluateDirectionalAxis(input, 0), Is.EqualTo(0),
                "The first active OR group contains Y only: a later group's X cannot join it.");
            Assert.That(mapping.EvaluateDirectionalAxis(input, 1), Is.EqualTo(short.MaxValue));
            input[3] = 0;
            Assert.That(mapping.EvaluateDirectionalAxis(input, 0), Is.EqualTo(short.MaxValue),
                "The second OR group can take over after the first group becomes inactive.");
        }

        [Test]
        public void PairedInputs_AreAlternativesAndSharedNotDoesNotRequireBothKeys()
        {
            var mapping = new Mapping
            {
                UseInputExpression = true,
                DeviceBindings = new List<DeviceBinding>
                {
                    new DeviceBinding { InputDirectionSlot = 1 },
                    new DeviceBinding { InputDirectionSlot = 2 }
                }
            };
            Assert.That(mapping.EvaluateInputExpression(new short[] { 1, 0 }), Is.EqualTo(1));
            Assert.That(mapping.EvaluateInputExpression(new short[] { 0, 1 }), Is.EqualTo(1));
            Assert.That(mapping.EvaluateInputExpression(new short[] { 0, 0 }), Is.EqualTo(0));
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 1, 1 }, 0), Is.EqualTo(0));

            // NOT belongs to the logical pair, never to only one physical slot.
            mapping.Plugins.Add(new AxisToAxis());
            Assert.That(mapping.SetExpressionNegated(mapping.DeviceBindings[0], true), Is.True);
            Assert.That(mapping.DeviceBindings.All(binding => binding.InputExpressionNegated), Is.True);
            Assert.That(mapping.EvaluateInputExpression(new short[] { 1, 0 }), Is.EqualTo(0));
            Assert.That(mapping.EvaluateInputExpression(new short[] { 0, 0 }), Is.EqualTo(1));
            Assert.That(mapping.EvaluateDirectionalAxis(new short[] { 1, 0 }, 0), Is.EqualTo(0),
                "NOT must never silently invert the analogue direction.");
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
