﻿using System.Threading.Tasks;

namespace Ididit.Backup.Online;

public interface IGoogleDriveBackup : IDataExport
{
    Task ImportData();
}
