# Thermo FAIMS to mzML Converter

## Overview

The Thermo FAIMS to MzML converts a Thermo .raw file with FAIMS scans into a series of .mzML
files, creating one .mzML file for each FAIMS compensation voltage (CV) value in
the .raw file.  This is similar to the [FAIMS-MzXML-Generator](https://github.com/PNNL-Comp-Mass-Spec/FAIMS-MzXML-Generator/releases) but that program creates .mzXML files instead of .mzML files.

### Background

MaxQuant currently does not process FAIMS data correctly if multiple compensation voltages are used through the experiment's duration.
Both this program and the [FAIMS-MzXML-Generator](https://github.com/PNNL-Comp-Mass-Spec/FAIMS-MzXML-Generator/releases) 
split a FAIMS-based Thermo .raw file into a set of files, each containing only scans collected using a single compensation voltage. 
However, as of March 2020, MaxQuant does not support reading .mzML files.  Still, the output files created by this program are still valid .mzML files and other downstream processing tools may benefit from the availability of these files.

## Syntax

```
ThermoFAIMStoMzML.exe 
 InputFilePath [-O:OutputDirectoryPath] 
 [-S] [-R:Levels] [-Timeout:Minutes] [-IE] 
 [-L] [-LogFile:LogFilePath] [-Preview]
 [/ParamFile:ParamFileName.conf] [/CreateParamFile]
```

The first argument specifies the input .raw file
* Either just enter the name, or use `-I:DatasetName.raw`
* Can contain the wildcard character, for example `-I:*.raw`
                      
Optionally use `-O` to specify the output directory path
* If omitted, the output files will

Use `-Timeout` to specify the maximum runtime (in minutes) for each call to MSConvert.exe
* The default is `-Timeout:5`
  * Use a larger value for large .Raw files with lots of scans and/or data
* This parameter is necessary because if MSConvert.exe encounters an error, the program freezes, waiting for the user to press Ctrl+C
  * By specifying a timeout, this software will terminate MSConvert.exe if it runs too long

Use `-S` (or `-Recurse`) to process files in the current directory and in its subdirectories
* Optionally specify the maximum depth using `-R`, for example `-R:2`
* The default is `-R:0` which means to recurse infinitely
 * `-R:1` effectively disables recursing
 * `-R:2` means to process the current directory and files in just this directory's subdirectories

Use `-IE` or `-IgnoreErrors` to ignore errors while recursively processing files
* This also applies when when processing files with a wildcard

Use `-L` to enable logging messages to a file.
* Optionally use `-LogFile:FilePath` to specify the log file path

Use `-Preview` to preview the commands that would be run

The processing options can be specified in a parameter file using `/ParamFile:Options.conf` or `/Conf:Options.conf`
* Define options using the format `ArgumentName=Value`
* Lines starting with `#` or `;` will be treated as comments
* Additional arguments on the command line can supplement or override the arguments in the parameter file

Use `/CreateParamFile` to create an example parameter file
* By default, the example parameter file content is shown at the console
* To create a file named Options.conf, use `/CreateParamFile:Options.conf`

## Contacts

Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) \
E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov\
Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/ \
Source code: https://github.com/PNNL-Comp-Mass-Spec/Thermo-FAIMS-to-MzML

## License

The Thermo FAIMS to mzML Converter is licensed under the 2-Clause BSD License; 
you may not use this program except in compliance with the License. You may obtain 
a copy of the License at https://opensource.org/licenses/BSD-2-Clause

Copyright 2020 Battelle Memorial Institute

RawFileReader reading tool. Copyright © 2016 by Thermo Fisher Scientific, Inc. All rights reserved.
