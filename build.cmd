@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /win32icon:assets/app.ico /resource:assets/app.ico,EvaMacroStudio.AppIcon /target:winexe /out:FishMarco.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll Common.cs MacroStudio.cs Editing.cs Sequences.cs FeatureTests.cs Interaction.cs LiveUI.cs BatchEditing.cs ColorScan.cs
if errorlevel 1 pause
