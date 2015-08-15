﻿using System;
using System.IO;
using AldursLab.WurmApi.Tests.Helpers;
using AldursLab.WurmApi.Tests.TempDirs;
using AldursLab.WurmApi.Tests.Tests.Modules;
using NUnit.Framework;

namespace AldursLab.WurmApi.Tests.Tests
{
    [TestFixture]
    class LogWriterTests : TestsBase
    {
        private DirectoryHandle testDir;
        private LogWriter monthlyWriter;
        private LogWriter dailyWriter;
        private string pathDaily;
        private string pathMonthly;

        [SetUp]
        public void Setup()
        {
            testDir = TempDirectoriesFactory.CreateEmpty();
            this.pathDaily = Path.Combine(testDir.AbsolutePath, "_event.2014-01-01.txt");
            this.pathMonthly = Path.Combine(testDir.AbsolutePath, "_event.2014-01.txt");
            monthlyWriter = new LogWriter(pathDaily, new DateTime(2014, 1, 1), false);
            dailyWriter = new LogWriter(pathMonthly, new DateTime(2014, 1, 1), false);
        }

        [Test]
        public void WritesCorrectlyFormattedEntries()
        {
            var validLogEntry1 = new LogEntry(new DateTime(2014, 1, 1, 1, 1, 1), "Source", "Contents1");
            var validLogEntry2 = new LogEntry(new DateTime(2014, 1, 1, 1, 1, 2), string.Empty, "Contents2");
            dailyWriter.WriteSection(new[] { validLogEntry1 }, true);
            dailyWriter.WriteSection(new[] { validLogEntry2 });
            monthlyWriter.WriteSection(new [] { validLogEntry1 }, true);
            monthlyWriter.WriteSection(new [] { validLogEntry2 });

            var dailyContents = File.ReadAllText(pathDaily);
            var monthlyContents = File.ReadAllText(pathMonthly);

            const string ExpectedDailyContents = "Logging started 2014-01-01\r\n[01:01:01] <Source> Contents1\r\n[01:01:02] Contents2\r\n";
            const string ExpectedMonthlyContents = "Logging started 2014-01-01\r\n[01:01:01] <Source> Contents1\r\n[01:01:02] Contents2\r\n";

            Expect(dailyContents, EqualTo(ExpectedDailyContents));
            Expect(monthlyContents, EqualTo(ExpectedMonthlyContents));
        }
    }
}
