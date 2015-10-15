using System;
using System.Collections.Generic;
using System.Linq;
using AldursLab.WurmApi.Modules.Wurm.LogReading;
using AldursLab.WurmApi.Modules.Wurm.LogsHistory.Heuristics;
using AldursLab.WurmApi.Utility;
using JetBrains.Annotations;

namespace AldursLab.WurmApi.Modules.Wurm.LogsHistory
{
    class LogsScanner
    {
        readonly IWurmLogFiles wurmLogFiles;
        readonly MonthlyLogFilesHeuristics monthlyHeuristics;
        readonly LogFileStreamReaderFactory streamReaderFactory;
        readonly IWurmApiLogger logger;
        readonly LogFileParserFactory logFileParserFactory;
        readonly IWurmApiConfig wurmApiConfig;

        readonly LogSearchParameters logSearchParameters;
        readonly JobCancellationManager cancellationManager;

        public LogsScanner(
            [NotNull] LogSearchParameters logSearchParameters, 
            [NotNull] JobCancellationManager cancellationManager,
            [NotNull] IWurmLogFiles wurmLogFiles,
            [NotNull] MonthlyLogFilesHeuristics monthlyHeuristics,
            [NotNull] LogFileStreamReaderFactory streamReaderFactory,
            [NotNull] IWurmApiLogger logger,
            [NotNull] LogFileParserFactory logFileParserFactory, 
            [NotNull] IWurmApiConfig wurmApiConfig)
        {
            if (logSearchParameters == null) throw new ArgumentNullException("logSearchParameters");
            if (cancellationManager == null) throw new ArgumentNullException("cancellationManager");
            if (wurmLogFiles == null) throw new ArgumentNullException("wurmLogFiles");
            if (monthlyHeuristics == null) throw new ArgumentNullException("monthlyHeuristics");
            if (streamReaderFactory == null) throw new ArgumentNullException("streamReaderFactory");
            if (logger == null) throw new ArgumentNullException("logger");
            if (logFileParserFactory == null) throw new ArgumentNullException("logFileParserFactory");
            if (wurmApiConfig == null) throw new ArgumentNullException("wurmApiConfig");
            this.logSearchParameters = logSearchParameters;
            this.cancellationManager = cancellationManager;
            this.wurmLogFiles = wurmLogFiles;
            this.monthlyHeuristics = monthlyHeuristics;
            this.streamReaderFactory = streamReaderFactory;
            this.logger = logger;
            this.logFileParserFactory = logFileParserFactory;
            this.wurmApiConfig = wurmApiConfig;
        }

        /// <summary>
        /// Extracts all entries spanning given scan parameters. 
        /// Extraction is performed on a separate thread.
        /// Returned entries are in descending timestamp order.
        /// </summary>
        /// <returns></returns>
        public ScanResult Scan()
        {
            logSearchParameters.AssertAreValid();
            var man = this.wurmLogFiles.GetForCharacter(new CharacterName(logSearchParameters.CharacterName));
            LogFileInfo[] logFileInfos =
                man.GetLogFiles(logSearchParameters.MinDate, logSearchParameters.MaxDate)
                   .Where(info => info.LogType == logSearchParameters.LogType).ToArray();

            cancellationManager.ThrowIfCancelled();

            CharacterMonthlyLogHeuristics characterHeuristics =
                monthlyHeuristics.GetForCharacter(new CharacterName(logSearchParameters.CharacterName));

            cancellationManager.ThrowIfCancelled();

            var result = GetEntries(logFileInfos, characterHeuristics);

            switch (logSearchParameters.ScanResultOrdering)
            {
                case ScanResultOrdering.Ascending:
                    return new ScanResult(result);
                case ScanResultOrdering.Descending:
                    result.Reverse();
                    return new ScanResult(result);
                default:
                    throw new Exception("Unsupported ScanResultOrdering value: " + logSearchParameters.ScanResultOrdering);
            }
        }

        private List<LogEntry> GetEntries(
            IEnumerable<LogFileInfo> logFileInfos,
            CharacterMonthlyLogHeuristics heuristicsFileMap)
        {
            var parsingHelper = logFileParserFactory.Create();

            List<LogEntry> result = new List<LogEntry>();
            IOrderedEnumerable<LogFileInfo> orderedLogFileInfos =
                logFileInfos.OrderBy(info => info.LogFileDate.DateTime);

            foreach (LogFileInfo logFileInfo in orderedLogFileInfos)
            {
                if (logFileInfo.LogFileDate.LogSavingType == LogSavingType.Monthly)
                {
                    ParseMonthlyFile(heuristicsFileMap, logFileInfo, result, parsingHelper);
                }
                else if (logFileInfo.LogFileDate.LogSavingType == LogSavingType.Daily)
                {
                    ParseDailyFile(logFileInfo, result, parsingHelper);
                }
                else
                {
                    logger.Log(
                        LogLevel.Warn,
                        string.Format(
                            "LogsScanner encountered and skipped file with unsupported saving type, type: {0}, file: {1}",
                            logFileInfo.LogFileDate.LogSavingType,
                            logFileInfo.FullPath),
                        this,
                        null);
                }

                cancellationManager.ThrowIfCancelled();
            }
            return result;
        }

        private void ParseMonthlyFile(
            CharacterMonthlyLogHeuristics heuristicsFileMap,
            LogFileInfo logFileInfo,
            List<LogEntry> result,
            LogFileParser logFileParser)
        {
            var heuristics = heuristicsFileMap.GetFullHeuristicsForMonth(logFileInfo);
            var dayToSearchFrom = GetMinDayToSearchFrom(logSearchParameters.MinDate, logFileInfo.LogFileDate.DateTime);
            var dayToSearchTo = GetMaxDayToSearchUpTo(logSearchParameters.MaxDate, logFileInfo.LogFileDate.DateTime);

            List<LogEntry> entries = new List<LogEntry>();

            LogFileStreamReader reader = null;
            try
            {
                for (int day = dayToSearchFrom; day <= dayToSearchTo; day++)
                {
                    var thisDayHeuristics = heuristics.GetForDay(day);

                    if (thisDayHeuristics.LinesLength == 0) continue;

                    if (reader == null)
                    {
                        if (heuristics.HasValidFilePositions)
                        {
                            reader = streamReaderFactory.Create(
                                logFileInfo.FullPath,
                                thisDayHeuristics.StartPositionInBytes);
                        }
                        else
                        {
                            reader = streamReaderFactory.CreateWithLineCountFastForward(
                                logFileInfo.FullPath,
                                thisDayHeuristics.TotalLinesSinceBeginFile);
                        }
                    }
                    var thisEntryDate = new DateTime(
                        logFileInfo.LogFileDate.DateTime.Year,
                        logFileInfo.LogFileDate.DateTime.Month,
                        day,
                        0,
                        0,
                        0);

                    int readLinesCount = 0;
                    List<string> allLines = new List<string>();

                    string currentLine;
                    while ((currentLine = reader.TryReadNextLine()) != null)
                    {
                        allLines.Add(currentLine);
                        readLinesCount++;
                        if (readLinesCount == thisDayHeuristics.LinesLength)
                        {
                            break;
                        }
                    }

                    IList<LogEntry> parsedLines = logFileParser.ParseLinesForDay(allLines,
                        thisEntryDate,
                        logFileInfo);

                    // reversing the array, so that latest entries are first
                    entries.AddRange(parsedLines);

                    cancellationManager.ThrowIfCancelled();
                }

                result.AddRange(entries);
            }
            finally
            {
                if (reader != null) reader.Dispose();
            }
        }

        private int GetMaxDayToSearchUpTo(DateTime to, DateTime logDateTime)
        {
            if (to.Year == logDateTime.Year && to.Month == logDateTime.Month)
            {
                return to.Day;
            }
            else
            {
                return DateTime.DaysInMonth(logDateTime.Year, logDateTime.Month);
            }
        }

        private int GetMinDayToSearchFrom(DateTime frm, DateTime logDateTime)
        {
            if (frm.Year == logDateTime.Year && frm.Month == logDateTime.Month)
            {
                return frm.Day;
            }
            else
            {
                return 1;
            }
        }

        private void ParseDailyFile(LogFileInfo logFileInfo, List<LogEntry> result, LogFileParser logFileParser)
        {
            List<string> allLines = new List<string>();
            using (var reader = streamReaderFactory.Create(logFileInfo.FullPath))
            {
                string currentLine;
                while ((currentLine = reader.TryReadNextLine()) != null)
                {
                    allLines.Add(currentLine);
                }
            }

            IList<LogEntry> parsedLines = logFileParser.ParseLinesForDay(
                allLines,
                logFileInfo.LogFileDate.DateTime,
                logFileInfo);
            // reversing the array, so that latest entries are first
            result.AddRange(parsedLines);
        }
    }
}