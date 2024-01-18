using PRISM;

namespace ThermoFAIMStoMzML
{
    internal class ThermoFAIMStoMzMLOptions
    {
        // Ignore Spelling: Sto

        [Option("InputFile", "I", ArgPosition = 1, Required = true, HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "The name (or path) of a Thermo .raw file to convert into .mzML files; can contain the wildcard character *")]
        public string InputDataFilePath { get; set; }

        [Option("OutputDirectory", "O", HelpShowsDefault = false,
            HelpText = "Output directory path; if omitted, the output files will be created in the program directory.")]
        public string OutputDirectoryPath { get; set; }

        [Option("RenumberScans", "Renumber", HelpShowsDefault = false,
            HelpText = "When true, update scan numbers so that the first scan is 1 and there are no scan gaps")]
        public bool RenumberScans { get; set; }

        [Option("ScanStart", "Start", HelpShowsDefault = false,
            HelpText = "When non-zero, the first scan number to include in the output file")]
        public int ScanStart { get; set; }

        [Option("ScanEnd", "End", HelpShowsDefault = false,
            HelpText = "When non-zero, the last scan number to include in the output file. If ScanStart is non-zero but ScanEnd is zero, will include all scans from ScanStart to the end of the file")]
        public int ScanEnd { get; set; }

        [Option("Timeout", HelpShowsDefault = false,
            HelpText = "Maximum runtime (in minutes) for each call to MSConvert.exe")]
        public int MSConvertTimeoutMinutes { get; set; }

        [Option("Recurse", "S", HelpShowsDefault = false,
            HelpText = "When true, process files in the current directory and in all of its subdirectories")]
        public bool RecurseDirectories { get; set; }

        [Option("RecurseLevels", "R", ArgExistsProperty = nameof(RecurseDirectories), HelpShowsDefault = false,
            HelpText = "When RecurseDirectories is true, this defines the levels to recurse;\n" +
                       "0 to recurse infinitely; 1 to not recurse; 2 to process the current directory and files in its subdirectories")]
        public int MaxLevelsToRecurse { get; set; }

        [Option("IgnoreErrors", "IE", HelpShowsDefault = false,
            HelpText = "When true, ignore errors while recursively processing files")]
        public bool IgnoreErrorsWhenRecursing { get; set; }

        [Option("LogMessages", "L", HelpShowsDefault = false,
            HelpText = "When true, log messages to a file.")]
        public bool CreateLogFile { get; set; }

        [Option("LogFilePath", "LogFile", HelpShowsDefault = false,
            HelpText = "File path for logging messages.")]
        public string LogFilePath { get; set; }

        [Option("Preview", HelpShowsDefault = false,
            HelpText = "Preview the commands that would be run")]
        public bool Preview { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ThermoFAIMStoMzMLOptions()
        {
            InputDataFilePath = string.Empty;
            OutputDirectoryPath = string.Empty;

            RenumberScans = false;
            ScanStart = 0;
            ScanEnd = 0;

            MSConvertTimeoutMinutes = 15;

            CreateLogFile = false;
            LogFilePath = string.Empty;

            RecurseDirectories = false;
            MaxLevelsToRecurse = 1;

            Preview = false;
        }

        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(InputDataFilePath))
            {
                ConsoleMsgUtils.ShowError($"ERROR: Input path must be provided and non-empty; \"{InputDataFilePath}\" was provided.");
                return false;
            }

            return true;
        }
    }
}
