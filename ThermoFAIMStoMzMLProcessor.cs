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
        // Ignore Spelling: cv, outfile

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
                    return ConvertFile(msConvertFile, inputFile, outputDirectory);

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

                // Disable loading the method
                // Method loading doesn't work on Linux and this program doesn't need that information
                var readerOptions = new ThermoReaderOptions {
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
                    var outputFile = new FileInfo(Path.Combine(outputDirectory.FullName, outputFileName));

                    var success = ConvertFile(msConvertFile, inputFile, outputFile, cvInfo.Value);

                    if (!success)
                        successOverall = false;
                    else
                        successTotal++;

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
        /// <param name="cvTextFilter">String to find in the scan filter, e.g. cv=-45.00</param>
        /// <returns>True if success, false an error</returns>
        private bool ConvertFile(FileInfo msConvertFile, FileInfo inputFile, FileInfo outputFile, string cvTextFilter)
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

            var arguments = string.Format(
                " --32 --mzML" +
                " --filter \"thermoScanFilter contains include {0}\"" +
                " --outfile {1}" +
                " {2}",
                cvTextFilter, outputFilePath, inputFilePath);

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

            // Wait for the job to complete
            var success = WaitForMSConvertToFinish(programRunner, msConvertFile, Options.MSConvertTimeoutMinutes);

            return success;
        }

        private bool GetCvValue(XRawFileIO reader, int scanNumber, out float cvValue, out string filterTextMatch, bool showWarnings = false)
        {
            cvValue = 0;
            filterTextMatch = string.Empty;

            if (!reader.GetScanInfo(scanNumber, out var scanInfo))
            {
                if (showWarnings)
                {
                    OnWarningEvent(string.Format("Scan {0} not found; skipping", scanNumber));
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

                    var success = ConvertFile(inputFile, outputDirectory);
                    return success;
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
        /// <param name="msConvertFile"></param>
        /// <param name="maxRuntimeMinutes">Maximum runtime, in minutes</param>
        /// <returns>True if success, false the maximum runtime was exceeded or an error occurred</returns>
        private bool WaitForMSConvertToFinish(ProgRunner programRunner, FileInfo msConvertFile, int maxRuntimeMinutes)
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
                    "{0} runtime surpassed {1} minutes; aborting.  Use /Timeout to allow MSConvert to run longer, e.g. /Timeout:10",
                    msConvertFile.Name, maxRuntimeMinutes));

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
