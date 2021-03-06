﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AldursLab.WurmApi.Tests.Helpers;
using NUnit.Framework;

namespace AldursLab.WurmApi.Tests.Tests.Modules.Wurm.ServerHistory
{
    [TestFixture]
    class WurmServerHistoryTests : WurmTests
    {
        protected IWurmServerHistory System => Fixture.WurmApiManager.WurmServerHistory;
        protected StubbableTime.StubScope ClockScope;

        private readonly CharacterName characterNameTestguy = new CharacterName("Testguy");
        private readonly CharacterName characterNameTestguytwo = new CharacterName("Testguytwo");

        [SetUp]
        public void Init()
        {
            ClockScope = TimeStub.CreateStubbedScope();
            ClockScope.OverrideNow(new DateTime(2014, 12, 14, 17, 10, 0));
            ClockScope.OverrideNowOffset(new DateTime(2014, 12, 14, 17, 10, 0));

            ClientMock.PopulateFromZip(Path.Combine(TestPaksZippedDirFullPath, "WurmServerHistory-wurmdir.7z"));
        }

        [TearDown]
        public void TearDown()
        {
            ClockScope.Dispose();
        }

        [TestFixture]
        public class TryGetServer : WurmServerHistoryTests
        {
            [Test]
            public void Gets()
            {
                var server = System.TryGetServer(characterNameTestguy, new DateTime(2014, 12, 14, 17, 3, 0));
                Expect(server, EqualTo(new ServerName("Exodus")));
            }

            [Test]
            public async Task GetsAsync()
            {
                var server = await System.TryGetServerAsync(characterNameTestguy, new DateTime(2014, 12, 14, 17, 3, 0));
                Expect(server, EqualTo(new ServerName("Exodus")));
            }

            [Test]
            public void CanBeCancelled()
            {
                var cancellationSource = new CancellationTokenSource();
                cancellationSource.Cancel();
                Assert.Throws<OperationCanceledException>(
                    () =>
                        System.TryGetServer(characterNameTestguy,
                            new DateTime(2014, 12, 14, 17, 3, 0),
                            cancellationSource.Token));
            }

            [Test]
            public void ThrowsWhenNoData()
            {
                Expect(System.TryGetServer(characterNameTestguy, new DateTime(2014, 12, 14, 17, 2, 0)), Null);
            }

            [Test]
            public void GetsWhenMoreData()
            {
                Expect(System.TryGetServer(characterNameTestguytwo, new DateTime(2014, 12, 14, 17, 0, 0)), Null);

                var server2 = System.TryGetServer(characterNameTestguytwo, new DateTime(2014, 12, 14, 17, 5, 0));
                Expect(server2, EqualTo(new ServerName("Exodus")));

                var server3 = System.TryGetServer(characterNameTestguytwo, new DateTime(2014, 12, 14, 17, 9, 59));
                Expect(server3, EqualTo(new ServerName("Chaos")));

                var currentServer1 = System.TryGetCurrentServer(characterNameTestguy);
                Expect(currentServer1, EqualTo(new ServerName("Exodus")));

                var currentServer2 = System.TryGetCurrentServer(characterNameTestguytwo);
                Expect(currentServer2, EqualTo(new ServerName("Chaos")));
            }

            [Test]
            public async Task GetsAfterLiveEvent()
            {
                // next day
                ClockScope.OverrideNow(new DateTime(2014, 12, 15, 3, 5, 0));
                ClockScope.OverrideNowOffset(new DateTime(2014, 12, 15, 3, 5, 0));

                // verify current
                var nameCurrent1 = await System.TryGetCurrentServerAsync(characterNameTestguy);
                Expect(nameCurrent1, EqualTo(new ServerName("Exodus")));

                // add live event
                var path = Path.Combine(
                    ClientMock.InstallDirectory.FullPath,
                    "players",
                    "Testguy",
                    "logs",
                    "_Event.2014-12.txt");
                var logwriter = new LogWriter(path, new DateTime(2014, 12, 1), true);
                //Trace.WriteLine("----- before write");
                //Trace.Write(File.ReadAllText(path));
                //Trace.WriteLine("-----");
                logwriter.WriteSection(
                    new Collection<LogEntry>()
                    {
                        new LogEntry(new DateTime(2014, 12, 15, 3, 4, 0), String.Empty,
                            "42 other players are online. You are on Abuzabi (765 totally in Wurm).")
                    }, true);
                //Trace.WriteLine("----- after write");
                //Trace.Write(File.ReadAllText(path));
                //Trace.WriteLine("-----");

                ClockScope.AdvanceTime(1);

                Thread.Sleep(2000);

                ClockScope.AdvanceTime(1);

                var nameBefore = await System.TryGetServerAsync(characterNameTestguy, new DateTime(2014, 12, 15, 3, 3, 0));
                Expect(nameBefore, EqualTo(new ServerName("Exodus")));
                var nameAfter = await System.TryGetServerAsync(characterNameTestguy, new DateTime(2014, 12, 15, 3, 5, 0));
                Expect(nameAfter, EqualTo(new ServerName("Abuzabi")));
                var nameCurrent2 = await System.TryGetCurrentServerAsync(characterNameTestguy);
                Expect(nameCurrent2, EqualTo(new ServerName("Abuzabi")));
            }
        }
    }
}
