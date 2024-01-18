using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PRISM;
using PRISM.FileProcessor;
using ThermoRawFileReader;

// ReSharper disable SuggestBaseTypeForParameter
namespace ThermoFAIMStoMzML
{
    internal class ThermoFAIMStoMzMLProcessor : ProcessFilesBase
    {
        // Ignore Spelling: cv, outfile, Sto

        /// <summary>
        /// This RegEx matches scan filters of the form
        /// FTMS + p NSI cv=-45.00 Full ms
        /// ITMS + c NSI cv=-65.00 r d Full ms2 438.7423@cid35.00
        /// </summary>
        private readonly Regex mCvMatcher = new("cv=(?<CV>[0-9.+-]+)");

        /// <summary>
        /// Keys in this dictionary are .raw file names
        /// Values are a list of scans that do not have cv= or have an invalid number after the equals sign
        /// </summary>
        /// <remarks>This is used to limit the number of warnings reported by GetCvValue</remarks>
        private readonly Dictionary<string, List<int>> mCvScanWarnings = new();

        private ThermoFAIMStoMzMLOptions Options { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ThermoFAIMStoMzMLProcessor(ThermoFAIMStoMzMLOptions options)
        {
            Options = options;
            UpdateSettings(options);
        }

        private void AddToScanWarningList(string readerRawFilePath, int scanNumber, string warningMessage)
        {
            if (mCvScanWarnings.TryGetValue(readerRawFilePath, out var scanNumbers))
            {
                scanNumbers.Add(scanNumber);
            }
            else
            {
                scanNumbers = new List<int> { scanNumber };
                mCvScanWarnings.Add(readerRawFilePath, scanNumbers);
            }

            if (scanNumbers.Count < 10 || scanNumber % 100 == 0)
            {
                OnWarningEvent(warningMessage);
            }
        }

        /// <summary>
        /// Convert a .raw file into .mzML files, creating one for each FAIMS compensation voltage value
        /// </summary>
        /// <param name="inputFile">Input file</param>
        /// <param name="outputDirectory">Output directory</param>
        /// <returns>True if success, false an error</returns>
        private bool ConvertFile(FileInfo inputFile, DirectoryInfo outputDirectory)
        {
            try
            {
                // This project has a reference to the PSI_Interface NuGet package because InformedProteomics refers to it
                // The reference is probably not required, but it doesn't hurt

                var proteowizardPath = InformedProteomics.Backend.MassSpecData.ProteoWizardReader.FindPwizPath();
                if (string.IsNullOrWhiteSpace(proteowizardPath))
                {
                    ShowWarning("Unable to find the installed location of ProteoWizard, which should have msconvert.exe");

                    ShowMessage("Typical locations for ProteoWizard:");
                    if (Environment.Is64BitProcess)
                    {
                        ShowMessage(@"C:\Program Files\ProteoWizard");
                        ShowMessage(@"C:\DMS_Programs\ProteoWizard");
                    }
                    else
                    {
                        ShowMessage(@"C:\Program Files (x86)\ProteoWizard");
                        ShowMessage(@"C:\DMS_Programs\ProteoWizard_x86");
                    }

                    return false;
                }

                var msConvertFile = new FileInfo(Path.Combine(proteowizardPath, "msconvert.exe"));

                if (msConvertFile.Exists)
                {
                    return ConvertFile(msConvertFile, inputFile, outputDirectory);
                }

                ShowWarning("Could not find msconvert.exe at " + msConvertFile.FullName);
                return false;
            }
            catch (Exception ex)
            {
                HandleException("Error in ConvertFile", ex);
                return false;
            }
        }

        /// <summary>
        /// Convert a .raw file into .mzML files, creating one for each FAIMS compensation voltage value
        /// </summary>
        /// <param name="msConvertFile">msconvert.exe</param>
        /// <param name="inputFile">Input file</param>
        /// <param name="outputDirectory">Output directory</param>
        /// <returns>True if success, false an error</returns>
        private bool ConvertFile(FileInfo msConvertFile, FileInfo inputFile, DirectoryInfo outputDirectory)
        {
            try
            {
                Console.WriteLine();
                ShowMessage("Opening " + inputFile.FullName);

                // Disable loading the MS method
                // Required since method loading doesn't work on Linux and this program doesn't need that information
                var readerOptions = new ThermoReaderOptions
                {
                    LoadMSMethodInfo = false
                };

                var reader = new XRawFileIO(inputFile.FullName, readerOptions);

                ShowMessage("Determining FAIMS CV values", false);

                var cvValues = GetUniqueCvValues(reader);

                if (cvValues.Count == 0)
                {
                    ShowWarning("File does not have any FAIMS scans with cv= in the scan filter");
                    return false;
                }

                var baseName = Path.GetFileNameWithoutExtension(inputFile.Name);

                var successOverall = true;
                var valuesProcessed = 0;
                var successTotal = 0;

                ShowMessage("Creating .mzML files", false);

                foreach (var cvInfo in cvValues)
                {
                    var percentComplete = valuesProcessed / (float)cvValues.Count * 100;

                    Console.WriteLine();
                    ShowMessage(string.Format(
                        "{0}% complete: FAIMS compensation voltage {1:F2}",
                        percentComplete, cvInfo.Key));

                    var outputFileName = string.Format("{0}_{1:F0}.mzML", baseName, cvInfo.Key);
                    var cvFilteredFile = new FileInfo(Path.Combine(outputDirectory.FullName, outputFileName));

                    var success = ConvertFile(msConvertFile, inputFile, cvFilteredFile, cvInfo.Key, cvInfo.Value);

                    if (!success)
                    {
                        successOverall = false;
                    }
                    else
                    {
                        if (Options.RenumberScans)
                        {
                            var renumbered = RenumberScans(cvFilteredFile, out var renumberedScansFile);

                            if (renumbered)
                            {
                                var indexSuccess = ReindexRenumberedFile(msConvertFile, cvFilteredFile, renumberedScansFile, Options.MSConvertTimeoutMinutes);

                                if (indexSuccess)
                                {
                                    renumberedScansFile.Delete();
                                }
                                else
                                {
                                    successOverall = false;
                                }
                            }
                            else
                            {
                                successOverall = false;
                            }
                        }

                        successTotal++;
                    }

                    valuesProcessed++;
                }

                Console.WriteLine();

                var actionDescription = Options.Preview ? "would create" : "created";

                ShowMessage(string.Format(
                    "100% complete: {0} {1} files in {2}", actionDescription, successTotal, outputDirectory.FullName));

                return successOverall;
            }
            catch (Exception ex)
            {
                HandleException("Error in ConvertFile", ex);
                return false;
            }
        }

        /// <summary>
        /// Convert a .raw file into a single .mzML file, including only the scans with the given FAIMS CV value
        /// </summary>
        /// <param name="msConvertFile">msconvert.exe</param>
        /// <param name="inputFile">Input .raw file</param>
        /// <param name="outputFile">Output .mzML file</param>
        /// <param name="cvValue">Compensation voltage value</param>
        /// <param name="cvTextFilter">String to find in the scan filter, e.g. cv=-45.00</param>
        /// <returns>True if success, false an error</returns>
        private bool ConvertFile(FileInfo msConvertFile, FileInfo inputFile, FileInfo outputFile, float cvValue, string cvTextFilter)
        {
            string inputFilePath;
            string outputFilePath;
            if (inputFile.DirectoryName?.Equals(outputFile.DirectoryName) == true)
            {
                inputFilePath = inputFile.Name;
                outputFilePath = outputFile.Name;
            }
            else
            {
                inputFilePath = inputFile.FullName;
                outputFilePath = outputFile.FullName;
            }

            string scanFilter;

            if (Options.ScanStart > 0 || Options.ScanEnd > 0)
            {
                // If ScanStart is 0 or 1 and ScanEnd is 1000, the scan filter will look like:
                // --filter "scanNumber [1,1000]"

                // If ScanStart is 30000 and ScanEnd is 0, the scan filter will look like:
                // --filter "scanNumber [30000-]"

                var startScan = Options.ScanStart == 0 ? 1 : Options.ScanStart;
                var endScan = Options.ScanEnd == 0 ? "-" : string.Format(",{0}", Options.ScanEnd);

                scanFilter = string.Format(" --filter \"scanNumber [{0}{1}]\"", startScan, endScan);
            }
            else
            {
                scanFilter = string.Empty;
            }

            var arguments = string.Format(
                " --32 --mzML --zlib" +
                " --filter \"thermoScanFilter contains include {0}\"" +
                "{1}" +
                " --outfile {2}" +
                " {3}",
                cvTextFilter, scanFilter, outputFilePath, inputFilePath);

            if (Options.Preview)
            {
                ShowDebugNoLog("Preview of call to " + msConvertFile.FullName);
                ShowDebugNoLog(string.Format("{0} {1}", msConvertFile.Name, arguments));
                return true;
            }

            ShowDebug("Processing file with MSConvert", false);
            ShowDebugNoLog(string.Format("{0} {1}", msConvertFile.Name, arguments));
            Console.WriteLine();

            var programRunner = new ProgRunner
            {
                CreateNoWindow = true,
                CacheStandardOutput = false,
                EchoOutputToConsole = true,
                WriteConsoleOutputToFile = false,
                Name = "MSConvert",
                Program = msConvertFile.FullName,
                Arguments = arguments,
                WorkDir = inputFile.DirectoryName
            };

            RegisterEvents(programRunner);

            programRunner.StartAndMonitorProgram();

            var currentTask = string.Format("creating the .mzML file for CV {0:F0}", cvValue);

            return WaitForMSConvertToFinish(programRunner, msConvertFile, currentTask, Options.MSConvertTimeoutMinutes);
        }

        private bool GetCvValue(XRawFileIO reader, int scanNumber, out float cvValue, out string filterTextMatch, bool showWarnings = false)
        {
            cvValue = 0;
            filterTextMatch = string.Empty;

            if (!reader.GetScanInfo(scanNumber, out var scanInfo))
            {
                if (showWarnings)
                {
                    OnWarningEvent("Scan {0} not found; skipping", scanNumber);
                }
                return false;
            }

            var filterText = scanInfo.FilterText;

            if (filterText.IndexOf("cv=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (showWarnings)
                {
                    AddToScanWarningList(
                        reader.RawFilePath, scanNumber,
                        string.Format("Scan {0} does not contain cv=; skipping", scanNumber));
                }
                return false;
            }

            var match = mCvMatcher.Match(filterText);

            if (!match.Success)
            {
                if (showWarnings)
                {
                    AddToScanWarningList(
                        reader.RawFilePath, scanNumber,
                        string.Format(
                            "Scan {0} has cv= in the filter text, but it is not followed by a number: {1}",
                            scanNumber, filterText));
                }

                return false;
            }

            if (!float.TryParse(match.Groups["CV"].Value, out cvValue))
            {
                if (showWarnings)
                {
                    AddToScanWarningList(
                        reader.RawFilePath, scanNumber,
                        string.Format(
                            "Unable to parse the CV value for scan {0}: {1}",
                            scanNumber, match.Groups["CV"].Value));
                }

                return false;
            }

            filterTextMatch = match.Value;
            return true;
        }

        /// <summary>
        /// Get the default file extensions to parse
        /// </summary>
        /// <returns>List of extensions</returns>
        public override IList<string> GetDefaultExtensionsToParse()
        {
            return new List<string>
            {
                ".raw"
            };
        }

        private Dictionary<float, string> GetUniqueCvValues(XRawFileIO reader)
        {
            // Dictionary where keys are CV values and values are the filter text that scans with this CV value will have
            var cvValues = new Dictionary<float, string>();

            // Dictionary where keys are CV values and values are the number of scans with this value
            // This is used when Options.Preview is true
            var cvValueStats = new Dictionary<float, int>();

            for (var scanNumber = reader.ScanStart; scanNumber <= reader.ScanEnd; scanNumber++)
            {
                var success = GetCvValue(reader, scanNumber, out var cvValue, out var filterTextMatch, true);
                if (!success)
                    continue;

                if (cvValues.ContainsKey(cvValue))
                {
                    var scanCount = cvValueStats[cvValue];
                    cvValueStats[cvValue] = scanCount + 1;
                    if (Options.Preview && scanCount > 50)
                    {
                        // If all of the CV values have been found 50+ times, assume that we have found all of the CV values
                        // (since typically machines cycle through a CV list)

                        var minObservedCount = (from item in cvValueStats select item.Value).Min();
                        if (minObservedCount > 50)
                        {
                            // Ignore the remaining scans
                            break;
                        }
                    }

                    continue;
                }

                cvValues.Add(cvValue, filterTextMatch);
                cvValueStats.Add(cvValue, 1);
            }

            return cvValues;
        }

        public override string GetErrorMessage()
        {
            return GetBaseClassErrorMessage();
        }

        public override bool ProcessFile(string inputFilePath, string outputDirectoryPath, string parameterFilePath, bool resetErrorCode)
        {
            if (resetErrorCode)
                SetBaseClassErrorCode(ProcessFilesErrorCodes.NoError);

            try
            {
                if (string.IsNullOrWhiteSpace(inputFilePath))
                {
                    ShowErrorMessage("Input directory name is empty");
                    SetBaseClassErrorCode(ProcessFilesErrorCodes.InvalidInputFilePath);
                    return false;
                }

                if (!CleanupFilePaths(ref inputFilePath, ref outputDirectoryPath))
                {
                    SetBaseClassErrorCode(ProcessFilesErrorCodes.FilePathError);
                    return false;
                }

                try
                {
                    // Obtain the full path to the input file
                    var inputFile = new FileInfo(inputFilePath);

                    if (string.IsNullOrWhiteSpace(mOutputDirectoryPath))
                    {
                        mOutputDirectoryPath = inputFile.DirectoryName;
                    }

                    if (string.IsNullOrWhiteSpace(mOutputDirectoryPath))
                    {
                        ShowErrorMessage("Parent directory is null for the output folder: " + mOutputDirectoryPath);
                        SetBaseClassErrorCode(ProcessFilesErrorCodes.InvalidOutputDirectoryPath);
                        return false;
                    }

                    var outputDirectory = new DirectoryInfo(mOutputDirectoryPath);

                    return ConvertFile(inputFile, outputDirectory);
                }
                catch (Exception ex)
                {
                    HandleException("Error calling ConvertFile", ex);
                    return false;
                }
            }
            catch (Exception ex)
            {
                HandleException("Error in ProcessFile", ex);
                return false;
            }
        }

        private bool ReindexRenumberedFile(
            FileInfo msConvertFile,
            FileInfo cvFilteredFile,
            FileInfo renumberedScansFile,
            int maxRuntimeMinutes = 120)
        {
            try
            {
                // Replace the original file with an indexed version of the file with renumbered scans

                ShowMessage(string.Format("Adding an index to file {0}", renumberedScansFile.Name));

                var finalFilePath = cvFilteredFile.FullName;

                cvFilteredFile.Delete();

                var arguments = string.Format("--mzML --zlib {0} --outfile {1}",
                    PathUtils.PossiblyQuotePath(renumberedScansFile.FullName),
                    PathUtils.PossiblyQuotePath(finalFilePath));

                if (Options.Preview)
                {
                    ShowDebugNoLog("Preview of call to " + msConvertFile.FullName);
                    ShowDebugNoLog(string.Format("{0} {1}", msConvertFile.Name, arguments));
                    return true;
                }

                var programRunner = new ProgRunner
                {
                    CreateNoWindow = true,
                    CacheStandardOutput = false,
                    EchoOutputToConsole = true,
                    WriteConsoleOutputToFile = false,
                    Name = "MSConvert",
                    Program = msConvertFile.FullName,
                    Arguments = arguments,
                    WorkDir = renumberedScansFile.DirectoryName
                };

                RegisterEvents(programRunner);

                programRunner.StartAndMonitorProgram();

                const string currentTask = "adding an index to the .mzML file with renumbered scans";

                return WaitForMSConvertToFinish(programRunner, msConvertFile, currentTask, maxRuntimeMinutes);
            }
            catch (Exception ex)
            {
                HandleException("Error re-indexing the renumbered .mzML file", ex);
                return false;
            }
        }

        private bool RenumberScans(FileInfo cvFilteredFile, out FileInfo renumberedScansFile)
        {
            try
            {
                ShowMessage(string.Format("Renumbering the spectra in file {0}", cvFilteredFile.Name));

                if (cvFilteredFile.Directory == null)
                {
                    throw new Exception("Cannot determine the parent directory of the input file: " + cvFilteredFile.FullName);
                }

                var updatedFileName = string.Format("{0}{1}", Path.GetFileNameWithoutExtension(cvFilteredFile.Name), "_renumbered.mzML");
                renumberedScansFile = new FileInfo(Path.Combine(cvFilteredFile.Directory.FullName, updatedFileName));

                // Option 1: use MzMLReader and MzMLWriter
                // This has the downside of caching the entire file in memory

                /*
                var reader = new PSI_Interface.MSData.mzML.MzMLReader(originalFile.FullName);
                var mzMLData = reader.Read();

                var writer = new MzMLWriter(updatedFile.FullName)
                {
                    MzMLType = MzMLSchemaType.MzML
                };

                var scanNumber = 0;

                foreach (var spectrum in mzMLData.run.spectrumList.spectrum)
                {
                    scanNumber++;

                    spectrum.id = string.Format("controllerType=0 controllerNumber=1 scan={0}", scanNumber);
                    spectrum.index = string.Format("{0}", scanNumber - 1);

                    foreach (var scanInfo in spectrum.scanList.scan)
                    {
                        scanInfo.spectrumRef = scanInfo.spectrumRef;
                    }
                }

                writer.Write(mzMLData);
                */

                // Option 2: use a forward-only XML reader to read/write the XML
                // This works, but given that the input file is from MSConvert, we can safely assume that it will be well-formatted

                // Option 3: use a simple text reader

                if (Options.Preview)
                {
                    ShowDebugNoLog(string.Format("Would next read {0} to create {1}", cvFilteredFile.Name, renumberedScansFile.Name));
                    return true;
                }

                var scanMatcher = new Regex(@"^(?<Prefix> *<spectrum index="")(?<Index>\d+)(?<ControllerInfo>"" id=""controllerType=\d+ controllerNumber=\d+ scan=)(?<Scan>\d+)(?<Suffix>"".+)");

                var scanNumber = 0;
                var skippedIndexMzMLElement = false;

                using var reader = new StreamReader(new FileStream(cvFilteredFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read));
                using var writer = new StreamWriter(new FileStream(renumberedScansFile.FullName, FileMode.Create, FileAccess.Write, FileShare.Read));

                writer.NewLine = "\n";

                while (!reader.EndOfStream)
                {
                    var dataLine = reader.ReadLine();

                    if (dataLine == null)
                        continue;

                    // ReSharper disable once StringLiteralTypo
                    if (!skippedIndexMzMLElement && dataLine.TrimStart().StartsWith("<indexedmzML"))
                    {
                        // Skip this line
                        skippedIndexMzMLElement = true;
                        continue;
                    }

                    var match = scanMatcher.Match(dataLine);

                    if (!match.Success)
                    {
                        if (dataLine.TrimStart().StartsWith("<spectrum "))
                        {
                            throw new Exception("Spectrum line did not match the expected pattern; unable to update the scan number for\n" + dataLine);
                        }

                        writer.WriteLine(dataLine);

                        if (dataLine.Trim().Equals("</mzML>"))
                        {
                            // The remaining lines are the index; skip them
                            break;
                        }

                        continue;
                    }

                    scanNumber++;

                    var updatedLine = string.Format("{0}{1}{2}{3}{4}",
                        match.Groups["Prefix"],
                        scanNumber - 1,
                        match.Groups["ControllerInfo"],
                        scanNumber,
                        match.Groups["Suffix"]);

                    writer.WriteLine(updatedLine);
                }

                return true;
            }
            catch (Exception ex)
            {
                HandleException("Error renumbering scans in the .mzML file", ex);
                renumberedScansFile = null;
                return false;
            }
        }

        private void ShowDebugNoLog(string message, int emptyLinesBeforeMessage = 0)
        {
            ConsoleMsgUtils.ShowDebugCustom(message, emptyLinesBeforeMessage: emptyLinesBeforeMessage);
        }

        /// <summary>
        /// Update settings using the options
        /// </summary>
        /// <param name="options"></param>
        public void UpdateSettings(ThermoFAIMStoMzMLOptions options)
        {
            mOutputDirectoryPath = options.OutputDirectoryPath;
            IgnoreErrorsWhenUsingWildcardMatching = options.IgnoreErrorsWhenRecursing;

            LogMessagesToFile = options.CreateLogFile;
            LogFilePath = options.LogFilePath;
        }

        /// <summary>
        /// Wait for the program runner to finish
        /// </summary>
        /// <param name="programRunner">Program runner instance</param>
        /// <param name="msConvertFile">msconvert.exe</param>
        /// <param name="currentTask">Description of the current task</param>
        /// <param name="maxRuntimeMinutes">Maximum runtime, in minutes</param>
        /// <returns>True if success, false the maximum runtime was exceeded or an error occurred</returns>
        private bool WaitForMSConvertToFinish(
            ProgRunner programRunner,
            FileInfo msConvertFile,
            string currentTask,
            int maxRuntimeMinutes)
        {
            var startTime = DateTime.UtcNow;
            var runtimeExceeded = false;

            // Loop until program is complete, or until MaxRuntimeSeconds elapses
            while (programRunner.State != ProgRunner.States.NotMonitoring)
            {
                AppUtils.SleepMilliseconds(2000);

                if (maxRuntimeMinutes <= 0)
                    continue;

                if (DateTime.UtcNow.Subtract(startTime).TotalMinutes < maxRuntimeMinutes)
                    continue;

                runtimeExceeded = true;
                break;
            }

            if (runtimeExceeded)
            {
                ShowErrorMessage(string.Format(
                    "{0} runtime surpassed {1} minutes while {2}; aborting. Use /Timeout to allow MSConvert to run longer, e.g. /Timeout:10",
                    msConvertFile.Name, maxRuntimeMinutes, currentTask));

                programRunner.StopMonitoringProgram(kill: true);
                return false;
            }

            if (programRunner.ExitCode == 0)
                return true;

            ShowWarning(string.Format(
                "{0} reported a non-zero return code: {1}",
                msConvertFile.Name, programRunner.ExitCode));

            return false;
        }
    }
}
