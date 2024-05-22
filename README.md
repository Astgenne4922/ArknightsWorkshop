# ArknightsWorkshop
Some CLI tools for Arknights datamining.
## Features
### Resource downloader
Get all resources (both ones that come with the game itself and ones that are downloaded by the game itself) without messing with Android emulator or spending a lot of time copying them from a real mobile device. This tool will download and un`zip` all assets of the current Global or China version to the `%WorkingDirectory%/assets/%version%/output` folder. _It can continue downloading after restarting or getting some connection errors_.
### More things soon!
## App configuration
Place `config.json` file next to executable. If it is missing, these defaults will be used:
```json
{
    // How many HTTP connections are allowed simultaneously.
    "MaxConcurrentDownloads": 4,
    // Save or delete intermediate data (for example, '.dat' and 'game.apk' 
    // files for resource downloader) after some tool is finished working.
    "KeepIntermediateData": true,
    // Folder in which all the data will be stored. When not specified,
    // 'resources' folder next to executable will be created and used.
    "WorkingDirectory": null,
}
```
## Using
Just download executable from **Actions** page. There are versions for `Windows`, `Linux` amd `Mac OS` running on `x64` or `arm64` CPUs.
## Developing
* Download [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0), use some IDE (Visual Studio or Rider) or .NET CLI to develop
* You can create `config.json` directly in the repository folder. It's `gitignore`d and will be automatically copied to the app when debugging.