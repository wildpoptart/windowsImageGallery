# WPF Application

A Windows desktop application built with WPF (.NET).

## Overview

This application provides a graphical user interface with a main window and settings functionality.

## Features

- Main application window
- Settings configuration window
- [Add more specific features once implemented]

## Requirements

- .NET Framework/Core [version]
- Windows OS

## FFmpeg Requirements

This application requires FFmpeg binaries to handle video thumbnails and playback. Please follow these steps:

1. Download FFmpeg binaries for Windows from https://ffmpeg.org/download.html
2. Create an "ffmpeg" folder in your application directory
3. Copy the following DLLs to the ffmpeg folder:
   - avcodec-XX.dll
   - avformat-XX.dll
   - avutil-XX.dll
   - swscale-XX.dll
   (where XX is the version number)

## FFmpeg Setup

1. Download FFmpeg shared binaries for Windows from https://github.com/BtbN/FFmpeg-Builds/releases
2. Create folders in your project:
   - `ffmpeg/x64/` for 64-bit DLLs
   - `ffmpeg/x86/` for 32-bit DLLs (if needed)
3. Copy these DLLs to the appropriate folder:
   - avcodec-58.dll
   - avdevice-58.dll
   - avfilter-7.dll
   - avformat-58.dll
   - avutil-56.dll
   - postproc-55.dll
   - swresample-3.dll
   - swscale-5.dll

Note: The version numbers (e.g., 58, 7, 56) might be different depending on the FFmpeg build you download.

## Development Setup

1. Clone the repository
2. Open the solution in Visual Studio
3. Build and run the application

## Project Structure

- `MainWindow.xaml` & `MainWindow.xaml.cs` - Main application window
- `SettingsWindow.xaml` & `SettingsWindow.xaml.cs` - Settings configuration window
- `App.xaml` & `App.xaml.cs` - Application entry point and global resources

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request
