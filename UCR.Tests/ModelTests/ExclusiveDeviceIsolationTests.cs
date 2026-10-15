using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HidWizards.UCR.Core.Models;
using NUnit.Framework;

namespace HidWizards.UCR.Tests.ModelTests
{
    [TestFixture]
    internal class ExclusiveDeviceIsolationTests
    {
        private static readonly Type ManagerType = typeof(Profile).Assembly.GetType(
            "HidWizards.UCR.Core.Managers.ExclusiveDeviceModeManager", true);

        private static object Call(string name, params object[] arguments)
        {
            return ManagerType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, arguments);
        }

        [Test]
        public void SymbolicHidPathResolvesToExactInstance()
        {
            var result = (string)Call("NormalizeInstancePath",
                @"\\?\HID#VID_1234&PID_5678#6&ABC&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}");
            Assert.That(result, Is.EqualTo(@"HID\VID_1234&PID_5678\6&ABC&0&0000"));
        }

        [Test]
        public void OrdinaryGamepadDoesNotHideItsUsbParent()
        {
            var result = (IEnumerable<string>)Call("ResolveControllerPaths",
                @"HID\VID_1234&PID_5678\abc", @"USB\VID_1234&PID_5678\parent", null, 1);
            CollectionAssert.AreEquivalent(new[] { @"HID\VID_1234&PID_5678\abc" }, result.ToArray());
        }

        [Test]
        public void XInputControllerIncludesHidUsbAndXusbInterfaces()
        {
            var result = (IEnumerable<string>)Call("ResolveControllerPaths",
                @"HID\VID_045E&PID_028E&IG_00\child",
                @"USB\VID_045E&PID_028E\parent",
                @"USB\VID_045E&PID_028E\companion", 1);
            CollectionAssert.AreEquivalent(new[]
            {
                @"HID\VID_045E&PID_028E&IG_00\child",
                @"USB\VID_045E&PID_028E\parent",
                @"USB\VID_045E&PID_028E\companion"
            }, result.ToArray());
        }

        [Test]
        public void CompositeUsbParentCannotBeHiddenAutomatically()
        {
            var exception = Assert.Throws<TargetInvocationException>(() =>
                Call("ResolveControllerPaths",
                    @"HID\VID_045E&PID_028E&IG_00\child",
                    @"USB\VID_045E&PID_028E\parent",
                    @"USB\VID_045E&PID_028E\companion", 3));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void MissingXInputParentIsNotSilentlyIgnored()
        {
            var exception = Assert.Throws<TargetInvocationException>(() =>
                Call("ResolveControllerPaths",
                    @"HID\VID_045E&PID_028E&IG_00\child", null, null, 1));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }
    }
}
