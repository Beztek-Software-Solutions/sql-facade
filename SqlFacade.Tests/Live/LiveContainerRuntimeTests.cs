// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.IO;
    using NUnit.Framework;

    [TestFixture]
    public class LiveContainerRuntimeTests
    {
        [Test]
        public void EnsureConfigured_IsIdempotent()
        {
            LiveContainerRuntime.EnsureConfigured();
            LiveContainerRuntime.EnsureConfigured();
            Assert.That(Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED"), Is.EqualTo("true"));
        }

        [Test]
        public void FindPodmanSocket_WhenPresent_ReturnsExistingPath()
        {
            string socket = LiveContainerRuntime.FindPodmanSocket();
            if (socket == null)
                Assert.Ignore("No Podman socket on this machine");
            Assert.That(File.Exists(socket), Is.True);
        }
    }
}
