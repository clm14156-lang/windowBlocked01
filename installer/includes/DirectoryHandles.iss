type
  TDirectoryFileInformation = record
    Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh: Cardinal;
    VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow: Cardinal;
  end;
  TDirectoryPin = record
    Handle: THandle;
    Path: String;
    Created: Boolean;
  end;

const
  InvalidHandle = -1;
  DirectoryAttribute = $10;
  ReparseAttribute = $400;
  ReadDirectory = $1;
  ShareReadWrite = $3;
  OpenExisting = 3;
  OpenDirectoryNoFollow = $02200000;
  ProcessRedirectionTrustPolicy = 16;

var
  ParentPins: array of TDirectoryPin;
  ClientHandle: THandle;
  ClientPinned: Boolean;

function OpenDirectoryHandle(const Name: String; Access, Share: Cardinal; Security: NativeUInt;
  Disposition, Flags: Cardinal; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function GetDirectoryInformation(Handle: THandle; var Info: TDirectoryFileInformation): Boolean;
  external 'GetFileInformationByHandle@kernel32.dll stdcall';
function CloseDirectoryHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function DirectoryErrorCode: Cardinal;
  external 'GetLastError@kernel32.dll stdcall';
function DirectoryAttributes(const Name: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function CreatePathDirectory(const Name: String; Security: NativeUInt): Boolean;
  external 'CreateDirectoryW@kernel32.dll stdcall';
function CurrentProcessHandle: THandle;
  external 'GetCurrentProcess@kernel32.dll stdcall';
function GetMitigationPolicy(Process: THandle; Policy: Integer; var Flags: Cardinal; BufferSize: NativeUInt): Boolean;
  external 'GetProcessMitigationPolicy@kernel32.dll stdcall';

function DirectoryRedirectionGuardEnabled: Boolean;
var
  Flags: Cardinal;
begin
  Flags := 0;
  Result := GetMitigationPolicy(CurrentProcessHandle, ProcessRedirectionTrustPolicy, Flags, 4) and (Flags and 1 <> 0);
end;

function DirectoryError(const Path: String; Code: Cardinal): String;
begin
  Result := Format('无法访问或创建目录 %s（Windows 错误 %d：%s）。', [Path, Code, SysErrorMessage(Code)]);
end;

function PinDirectory(const Path: String; var Handle: THandle): String;
var
  Info: TDirectoryFileInformation;
begin
  Result := '';
  { Deny deleting or renaming the opened directory while permitting child-file installation.
    RedirectionGuard and the explicit reparse checks also apply to these paths. }
  Handle := OpenDirectoryHandle(Path, ReadDirectory, ShareReadWrite, 0, OpenExisting, OpenDirectoryNoFollow, 0);
  if Handle = InvalidHandle then
    Result := DirectoryError(Path, DirectoryErrorCode)
  else
  begin
    if not GetDirectoryInformation(Handle, Info) then
      Result := DirectoryError(Path, DirectoryErrorCode)
    else if Info.Attributes and ReparseAttribute <> 0 then
      Result := '安装路径中存在符号链接、目录联接点或挂载点：' + Path
    else if Info.Attributes and DirectoryAttribute = 0 then
      Result := '安装路径中的项目不是文件夹：' + Path;
    if Result <> '' then CloseDirectoryHandle(Handle);
  end;
end;

function PinClientParents(const Path: String; CreateMissing: Boolean): String;
var
  Part: String;
  Index, Count: Integer;
  Pin: TDirectoryPin;
  Code: Cardinal;
begin
  Index := 3;
  while Index < Length(Path) do
  begin
    if Path[Index] = '\' then
    begin
      Part := Copy(Path, 1, Index - 1);
      if Index = 3 then Part := Part + '\';
      Pin.Path := Part;
      Pin.Created := False;
      if CreateMissing and (DirectoryAttributes(Part) = $FFFFFFFF) then
      begin
        Code := DirectoryErrorCode;
        if (Code <> 2) and (Code <> 3) then
        begin
          Result := DirectoryError(Part, Code);
          Exit;
        end;
        if not CreatePathDirectory(Part, 0) then
        begin
          Result := DirectoryError(Part, DirectoryErrorCode);
          Exit;
        end;
        Pin.Created := True;
      end;
      Result := PinDirectory(Part, Pin.Handle);
      if Result <> '' then Pin.Handle := 0;
      Count := GetArrayLength(ParentPins);
      SetArrayLength(ParentPins, Count + 1);
      ParentPins[Count] := Pin;
      if Result <> '' then Exit;
    end;
    Index := Index + 1;
  end;
  Result := '';
end;

function PinClient(const Path: String): String;
begin
  Result := PinDirectory(Path, ClientHandle);
  ClientPinned := Result = '';
end;

procedure ReleaseClient;
begin
  if ClientPinned then CloseDirectoryHandle(ClientHandle);
  ClientPinned := False;
end;

procedure ReleaseClientParents(RemoveCreated: Boolean);
var
  I: Integer;
begin
  for I := GetArrayLength(ParentPins) - 1 downto 0 do
  begin
    if ParentPins[I].Handle <> 0 then CloseDirectoryHandle(ParentPins[I].Handle);
    if RemoveCreated and ParentPins[I].Created then RemoveDir(ParentPins[I].Path);
  end;
  SetArrayLength(ParentPins, 0);
end;
