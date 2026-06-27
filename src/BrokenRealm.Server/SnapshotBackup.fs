namespace BrokenRealm.Server

open System
open System.IO

module SnapshotBackup =
    let private backupExtension = ".json"

    let backupDirectoryFor (snapshotPath: string) =
        let dataDirectory = Path.GetDirectoryName snapshotPath

        if String.IsNullOrWhiteSpace dataDirectory then
            "backups"
        else
            Path.Combine(dataDirectory, "backups")

    let validateBackupFileName (fileName: string) =
        if String.IsNullOrWhiteSpace fileName then
            Error "Backup file name is required."
        elif fileName.Contains("..") then
            Error "Backup file names may not contain parent-directory segments."
        elif fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 then
            Error "Backup file name contains invalid characters."
        elif not (fileName.EndsWith(backupExtension, StringComparison.OrdinalIgnoreCase)) then
            Error "Backup file names must end with .json."
        else
            Ok fileName

    let private backupPath backupDirectory fileName =
        Path.GetFullPath(Path.Combine(backupDirectory, fileName))

    let private ensureInsideDirectory (rootDirectory: string) (candidatePath: string) =
        let normalizedRoot = Path.GetFullPath(rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        let normalizedCandidate = Path.GetFullPath candidatePath
        let prefix = normalizedRoot + string Path.DirectorySeparatorChar

        if normalizedCandidate = normalizedRoot || normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
            Ok normalizedCandidate
        else
            Error "Backup path escapes the backup directory."

    let create (snapshotPath: string) (snapshot: GameSnapshot) (clock: unit -> DateTimeOffset) =
        let backupDirectory = backupDirectoryFor snapshotPath
        Directory.CreateDirectory(backupDirectory) |> ignore
        let timestamp = clock().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'")
        let fileName = $"game-snapshot-{timestamp}{backupExtension}"
        let destination = backupPath backupDirectory fileName

        match ensureInsideDirectory backupDirectory destination with
        | Error error -> Error error
        | Ok path ->
            SnapshotCodec.writeFile path snapshot
            Ok fileName

    let list (snapshotPath: string) =
        let backupDirectory = backupDirectoryFor snapshotPath

        if not (Directory.Exists backupDirectory) then
            []
        else
            Directory.EnumerateFiles(backupDirectory, $"*{backupExtension}")
            |> Seq.map (fun path ->
                let info = FileInfo(path)

                { fileName = info.Name
                  createdAt = DateTimeOffset info.LastWriteTimeUtc })
            |> Seq.sortByDescending _.createdAt
            |> Seq.toList

    let resolveBackupPath (snapshotPath: string) (fileName: string) =
        match validateBackupFileName fileName with
        | Error error -> Error error
        | Ok validatedFileName ->
            let backupDirectory = backupDirectoryFor snapshotPath
            let sourcePath = backupPath backupDirectory validatedFileName
            ensureInsideDirectory backupDirectory sourcePath