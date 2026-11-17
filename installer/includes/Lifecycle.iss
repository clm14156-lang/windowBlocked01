const
  ErrorPrefix = 'FOCUSTIME_ERROR: ';
  TargetPrefix = 'FOCUSTIME_TARGET: ';
  UpgradeMarker = 'FOCUSTIME_UPGRADE';

var
  PreparingStarted, Applied, Committed, InstallFailed, Applying, Upgrade: Boolean;
  LifecycleDetail, PreparedDirectory, PlannedDirectory: String;
  UninstallLock: THandle;

function CreateInstallMutex(Security: NativeUInt; InitialOwner: Boolean; const Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';

function ErrorDetail(const Detail, Code, MessageText: String): String;
var
  Prefix: String;
begin
  Prefix := 'FOCUSTIME_' + Code + ': ';
  Result := '';
  if Pos(Prefix, Detail) = 1 then Result := MessageText + #13#10 +
    Copy(Detail, Length(Prefix) + 1, Length(Detail));
end;

procedure ReadLifecycleOutput(const Line: String; const IsError, FirstLine: Boolean);
var
  Detail, MessageText: String;
begin
  Log(Line);
  if Pos(TargetPrefix, Line) = 1 then
    PlannedDirectory := Copy(Line, Length(TargetPrefix) + 1, Length(Line))
  else if Line = UpgradeMarker then Upgrade := True
  else if Pos(ErrorPrefix, Line) = 1 then
  begin
    Detail := Copy(Line, Length(ErrorPrefix) + 1, Length(Line));
    MessageText := ErrorDetail(Detail, 'PATH', '请输入本地磁盘下的有效文件夹路径，目录名不能包含保留名称或非法字符。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'VOLUME', '磁盘未就绪，或这是网络映射盘、虚拟盘符。请选择已挂载的本地卷。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'FILESYSTEM', '此文件系统不支持本安装器需要的目录权限。请选择 NTFS 或 ReFS。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'REPARSE', '路径中有符号链接、目录联接点或卷挂载点。请选择实际目录。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'NOT_DIRECTORY', '此路径中的项目是文件。请选择文件夹。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'ACCESS', 'Windows 拒绝读取或写入目录。请检查目录访问权限。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'UNSAFE_PATH', '目录或现有程序文件可由普通用户替换，无法安全地以管理员权限处理这些文件。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'MISSING_PARENT', '系统服务目录的上级目录不存在。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'OCCUPIED_PATH', '实际安装目录已有其他文件。请选择其他目录，或指定一个新的子目录。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'TOO_LONG', '安装路径过长。请选择层级更少或名称更短的目录。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'RESERVED', '该路径用于专注时光 Windows 服务。请为客户端选择其他目录。');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'RELOCATION', '覆盖升级需要使用当前安装目录。若要换位置，请先卸载旧版本。当前目录：');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'DOWNGRADE', '已安装更高版本，请使用同版本或更高版本的安装包。当前版本：');
    if MessageText = '' then MessageText := ErrorDetail(Detail, 'FOREIGN_SERVICE', '发现不属于本安装的专注时光服务。请先检查现有服务的程序路径：');
    if Detail = 'FOCUSTIME_CLIENTS_RUNNING' then
      MessageText := '请先结束专注，并在各个已登录的 Windows 会话中从托盘退出专注时光，再重试。';
    if MessageText = '' then MessageText := Detail;
    LifecycleDetail := MessageText;
  end
  else if IsError and (LifecycleDetail = '') then LifecycleDetail := Line;
end;

function RunLifecycle(const Action, Script, InstallDirectory: String): Boolean;
var
  Parameters: String;
  ExitCode: Integer;
begin
  LifecycleDetail := '';
  ExitCode := 0;
  Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + AddQuotes(Script) +
    ' -Action ' + Action + ' -InstallDirectory ' + AddQuotes(InstallDirectory) +
    ' -TransactionDirectory ' + AddQuotes(ExpandConstant('{tmp}\FocusTimeTransaction')) +
    ' -ReleaseVersion {#AppReleaseVersion}';
  if Action = 'Plan' then
    Parameters := Parameters + ' -PayloadManifest ' + AddQuotes(ExpandConstant('{tmp}\FocusTime.ClientFiles.txt'));
  if Action = 'Install' then
    Parameters := Parameters + ' -ServiceScript ' + AddQuotes(ExpandConstant('{tmp}\Install-FocusAppService.ps1')) +
      ' -ServiceArchive ' + AddQuotes(ExpandConstant('{tmp}\FocusTime.ServicePayload.zip'));
  Result := ExecAndLogOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters, ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, ExitCode, @ReadLifecycleOutput);
  Result := Result and (ExitCode = 0);
  if not Result and (LifecycleDetail = '') then LifecycleDetail := '安装检查未能完成。错误代码 ' + IntToStr(ExitCode);
end;

function TemporaryLifecycle: String;
begin
  Result := ExpandConstant('{tmp}\InstallerLifecycle.ps1');
end;

function InstalledLifecycle: String;
begin
  Result := ExpandConstant('{#ServiceDirectory}\InstallerLifecycle.ps1');
end;

function SelectInstallDirectory(const Selected: String): Boolean;
var
  Candidate: String;
begin
  Candidate := Trim(Selected);
  while (Length(Candidate) > 3) and (Candidate[Length(Candidate)] = '\') do
    SetLength(Candidate, Length(Candidate) - 1);
  PlannedDirectory := '';
  Upgrade := False;
  Result := False;
  if (Length(Candidate) < 4) or (Pos('"', Candidate) <> 0) or (Pos(#13, Candidate) <> 0) or
      (Pos(#10, Candidate) <> 0) then
    LifecycleDetail := '请输入本地磁盘下的完整文件夹路径。'
  else if RunLifecycle('Plan', TemporaryLifecycle, Candidate) then
  begin
    if PlannedDirectory = '' then LifecycleDetail := '安装检查没有返回实际安装目录。'
    else
    begin
      if (CompareText(Candidate, PlannedDirectory) <> 0) and not WizardSilent then
        if SuppressibleMsgBox('所选目录已有文件。专注时光将安装到子目录：' + #13#10 + PlannedDirectory,
            mbConfirmation, MB_OKCANCEL, IDOK) <> IDOK then Exit;
      WizardForm.DirEdit.Text := PlannedDirectory;
      Result := True;
      Exit;
    end;
  end;
  SuppressibleMsgBox('无法开始安装。' + #13#10 + LifecycleDetail, mbError, MB_OK, IDOK);
end;

procedure ExtractLifecycle;
begin
  ExtractTemporaryFile('InstallerLifecycle.ps1');
  ExtractTemporaryFile('FocusAppInstallDirectory.ps1');
  ExtractTemporaryFile('Install-FocusAppService.ps1');
  ExtractTemporaryFile('FocusTime.ClientFiles.txt');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not DirectoryRedirectionGuardEnabled then
  begin
    Result := 'Windows 未能启用目录重定向保护，安装已停止。';
    Exit;
  end;
  PreparedDirectory := ExpandConstant('{app}');
  if (PlannedDirectory = '') or (CompareText(PreparedDirectory, PlannedDirectory) <> 0) then
  begin
    Result := '实际安装目录与已检查的路径不一致。请返回并重新选择目录。';
    Exit;
  end;
  Log('FocusTime client directory: ' + PreparedDirectory);
  Log('FocusTime service directory: ' + ExpandConstant('{#ServiceDirectory}'));

  LifecycleDetail := PinClientParents(PreparedDirectory, not Upgrade);
  if (LifecycleDetail = '') and Upgrade then LifecycleDetail := PinClient(PreparedDirectory);
  if LifecycleDetail = '' then
  begin
    PreparingStarted := True;
    if RunLifecycle('Prepare', TemporaryLifecycle, PreparedDirectory) then
    begin
      if not Upgrade then LifecycleDetail := PinClient(PreparedDirectory);
      if LifecycleDetail = '' then RunLifecycle('Verify', TemporaryLifecycle, PreparedDirectory);
    end;
  end;
  if LifecycleDetail <> '' then Result := '无法开始安装。' + #13#10 + LifecycleDetail;
end;

procedure InstallService;
begin
  Applying := True;
  ShowServiceProgress;
  try
    ExtractTemporaryFile('FocusTime.ServicePayload.zip');
    Applied := RunLifecycle('Install', TemporaryLifecycle, PreparedDirectory);
  finally
    Applying := False;
  end;
  if not Applied then
  begin
    InstallFailed := True;
    SuppressibleMsgBox('专注时光安装失败，正在恢复安装前的状态。' + #13#10 +
      LifecycleDetail + #13#10 + '安装日志：' + ExpandConstant('{log}'), mbError, MB_OK, IDOK);
    Abort;
  end;
  ShowClientProgress;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then InstallService;
  if (CurStep = ssPostInstall) and Applied then
  begin
    Committed := True;
    if not RunLifecycle('Commit', TemporaryLifecycle, PreparedDirectory) then
      SuppressibleMsgBox('已安装。部分旧版文件清理失败。' + #13#10 + LifecycleDetail, mbInformation, MB_OK, IDOK);
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if Applying then
  begin
    Cancel := False;
    Confirm := False;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if InstallFailed then Result := 10 else Result := 0;
end;

procedure DeinitializeSetup;
begin
  if not Upgrade then ReleaseClient;
  if PreparingStarted and not Committed then
    if not RunLifecycle('Rollback', TemporaryLifecycle, PreparedDirectory) then
      SuppressibleMsgBox('安装清理未完成。请保存安装日志。' + #13#10 +
        LifecycleDetail + #13#10 + ExpandConstant('{log}'), mbError, MB_OK, IDOK);
  ReleaseClient;
  ReleaseClientParents(not Committed);
end;

function InitializeUninstall: Boolean;
begin
  if not DirectoryRedirectionGuardEnabled then
  begin
    Result := False;
    SuppressibleMsgBox('Windows 未能启用目录重定向保护，卸载已停止。', mbError, MB_OK, IDOK);
    Exit;
  end;
  UninstallLock := CreateInstallMutex(0, False, 'Global\FocusApp.Setup');
  if (UninstallLock = 0) or (DirectoryErrorCode = 183) then
  begin
    Result := False;
    SuppressibleMsgBox('请先关闭正在运行的安装或卸载程序。', mbError, MB_OK, IDOK);
    Exit;
  end;
  PreparedDirectory := ExpandConstant('{app}');
  LifecycleDetail := PinClientParents(PreparedDirectory, False);
  if (LifecycleDetail = '') and (DirectoryAttributes(PreparedDirectory) <> $FFFFFFFF) then
    LifecycleDetail := PinClient(PreparedDirectory);
  Result := LifecycleDetail = '';
  if Result then Result := RunLifecycle('CheckUninstall', InstalledLifecycle, PreparedDirectory);
  if not Result then
    SuppressibleMsgBox('无法卸载专注时光。' + #13#10 + LifecycleDetail, mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    if not RunLifecycle('Uninstall', InstalledLifecycle, PreparedDirectory) then
    begin
      SuppressibleMsgBox('无法停止或移除专注时光服务。' + #13#10 + LifecycleDetail, mbError, MB_OK, IDOK);
      Abort;
    end;
  if (CurUninstallStep = usPostUninstall) and ClientPinned then
  begin
    ReleaseClient;
    RemoveDir(PreparedDirectory);
  end;
end;

procedure DeinitializeUninstall;
begin
  ReleaseClient;
  ReleaseClientParents(False);
  if UninstallLock <> 0 then CloseDirectoryHandle(UninstallLock);
end;
