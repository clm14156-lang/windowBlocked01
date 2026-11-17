const
  Orange = $057EFD;
  TitleColor = $2F2118;
  SecondaryColor = $856766;
  TrackColor = $F0EFED;

var
  SettingsPage: TWizardPage;
  DirectoryEdit: TNewEdit;
  DesktopCheck, LaunchCheck: TNewCheckBox;
  ProgressStatus, PercentLabel: TNewStaticText;
  ProgressTrack, ProgressFill: TPanel;
  WelcomePrimary, SettingsPrimary, DonePrimary: TBitmapButton;
  CustomInstall: Boolean;
  LogoPath: String;

function SelectInstallDirectory(const Selected: String): Boolean; forward;
procedure ExtractLifecycle; forward;

function AddText(Parent: TWinControl; Text: String; X, Y, Width, Size, Color: Integer;
  Center, Bold: Boolean): TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  Result.Parent := Parent;
  Result.AutoSize := False;
  Result.SetBounds(X, Y, Width, ScaleY(Size * 2));
  Result.Caption := Text;
  Result.Font.Name := 'Microsoft YaHei UI';
  Result.Font.Size := Size;
  Result.Font.Color := Color;
  if Bold then Result.Font.Style := [fsBold];
  if Center then Result.Alignment := taCenter;
end;

procedure AddLogo(Parent: TWinControl; Y, Size: Integer);
var
  Logo: TBitmapImage;
begin
  Logo := TBitmapImage.Create(WizardForm);
  Logo.Parent := Parent;
  Logo.SetBounds((Parent.Width - ScaleX(Size)) div 2, ScaleY(Y), ScaleX(Size), ScaleY(Size));
  Logo.Stretch := True;
  Logo.PngImage.LoadFromFile(LogoPath);
end;

function AddPrimary(Parent: TWinControl; Text: String; X, Y, Width: Integer;
  OnClick: TNotifyEvent): TBitmapButton;
var
  Canvas: TCanvas;
  Background: TRect;
  W, H: Integer;
begin
  Result := TBitmapButton.Create(WizardForm);
  Result.Parent := Parent;
  Result.Caption := Text;
  Result.Cursor := crHand;
  Result.SetBounds(X, Y, Width, ScaleY(42));
  Result.TabStop := True;
  Result.OnClick := OnClick;
  W := Width - 4;
  H := Result.Height - 4;
  Result.Bitmap.Width := W;
  Result.Bitmap.Height := H;
  Canvas := Result.Bitmap.Canvas;
  Canvas.Brush.Color := clWhite;
  Background.Left := 0;
  Background.Top := 0;
  Background.Right := W;
  Background.Bottom := H;
  Canvas.FillRect(Background);
  Canvas.Brush.Color := Orange;
  Canvas.Pen.Color := Orange;
  Canvas.RoundRect(0, 0, W, H, ScaleX(12), ScaleY(12));
  Canvas.Font.Name := 'Microsoft YaHei UI';
  Canvas.Font.Size := 11;
  Canvas.Font.Style := [fsBold];
  Canvas.Font.Color := clWhite;
  Canvas.Brush.Style := bsClear;
  Canvas.TextOut((W - Canvas.TextWidth(Text)) div 2, (H - Canvas.TextHeight(Text)) div 2, Text);
end;

function AddSecondary(Parent: TWinControl; Text: String; X, Y, Width: Integer;
  OnClick: TNotifyEvent): TNewButton;
begin
  Result := TNewButton.Create(WizardForm);
  Result.Parent := Parent;
  Result.SetBounds(X, Y, Width, ScaleY(32));
  Result.Caption := Text;
  Result.OnClick := OnClick;
end;

procedure GoNext(Sender: TObject);
begin
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure GoBack(Sender: TObject);
begin
  WizardForm.BackButton.OnClick(WizardForm.BackButton);
end;

procedure OpenSettings(Sender: TObject);
begin
  CustomInstall := True;
  DirectoryEdit.Text := WizardDirValue;
  GoNext(Sender);
end;

procedure InstallDefault(Sender: TObject);
begin
  CustomInstall := False;
  GoNext(Sender);
end;

procedure ChooseDirectory(Sender: TObject);
var
  Directory: String;
begin
  Directory := DirectoryEdit.Text;
  if BrowseForFolder('选择安装位置', Directory, False) then DirectoryEdit.Text := Directory;
end;

procedure HideChildren(Parent: TWinControl);
var
  I: Integer;
begin
  for I := 0 to Parent.ControlCount - 1 do Parent.Controls[I].Hide;
end;

function AddBar(Parent: TWinControl; Color, X, Y, W, H: Integer): TPanel;
begin
  Result := TPanel.Create(WizardForm);
  Result.Parent := Parent;
  Result.StyleElements := [];
  Result.ParentBackground := False;
  Result.BevelOuter := bvNone;
  Result.Color := Color;
  Result.SetBounds(X, Y, W, H);
end;

procedure InitializeWizard;
var
  Welcome, Installing, Finished, Settings: TWinControl;
  W, LeftEdge, ContentWidth, ButtonY: Integer;
  LaunchButtonWidth: Integer;
begin
  WizardForm.Caption := '专注时光 安装程序';
  WizardForm.BorderStyle := bsSingle;
  WizardForm.BorderIcons := [biSystemMenu, biMinimize];
  WizardForm.Color := clWhite;
  WizardForm.MainPanel.Hide;
  ExtractTemporaryFile('FocusTimeLogo.png');
  LogoPath := ExpandConstant('{tmp}\FocusTimeLogo.png');
  ExtractLifecycle;

  Welcome := WizardForm.WelcomePage;
  HideChildren(Welcome);
  WizardForm.WelcomePage.Color := clWhite;
  W := Welcome.Width;
  ButtonY := Welcome.Height - ScaleY(105);
  AddLogo(Welcome, 40, 77);
  AddText(Welcome, '安装 专注时光', 0, ScaleY(131), W, 22, TitleColor, True, True);
  AddText(Welcome, '一款专注与屏蔽工具', 0, ScaleY(175), W, 11, SecondaryColor, True, False);
  WelcomePrimary := AddPrimary(Welcome, '立即安装', (W - ScaleX(236)) div 2, ButtonY,
    ScaleX(236), @InstallDefault);
  AddSecondary(Welcome, '自定义安装', (W - ScaleX(142)) div 2, ButtonY + ScaleY(51),
    ScaleX(142), @OpenSettings);

  SettingsPage := CreateCustomPage(wpWelcome, '安装设置', '');
  Settings := SettingsPage.Surface;
  W := SettingsPage.SurfaceWidth;
  LeftEdge := ScaleX(12);
  ContentWidth := W - 2 * LeftEdge;
  AddText(Settings, '安装设置', LeftEdge, ScaleY(44), ContentWidth, 21, TitleColor, False, True);
  AddText(Settings, '选择专注时光的安装位置', LeftEdge, ScaleY(83), ContentWidth, 11, SecondaryColor, False, False);
  DirectoryEdit := TNewEdit.Create(WizardForm);
  DirectoryEdit.Parent := Settings;
  DirectoryEdit.SetBounds(LeftEdge, ScaleY(126), ContentWidth - ScaleX(85), ScaleY(29));
  DirectoryEdit.Text := WizardDirValue;
  AddSecondary(Settings, '浏览...', W - LeftEdge - ScaleX(75), ScaleY(124), ScaleX(75), @ChooseDirectory);
  DesktopCheck := TNewCheckBox.Create(WizardForm);
  DesktopCheck.Parent := Settings;
  DesktopCheck.Caption := '创建桌面快捷方式';
  DesktopCheck.SetBounds(LeftEdge, ScaleY(178), ContentWidth, ScaleY(24));
  DesktopCheck.Checked := True;
  ButtonY := SettingsPage.SurfaceHeight - ScaleY(55);
  AddSecondary(Settings, '上一步', LeftEdge, ButtonY + ScaleY(5), ScaleX(122), @GoBack);
  SettingsPrimary := AddPrimary(Settings, '立即安装', W - LeftEdge - ScaleX(155), ButtonY, ScaleX(155), @GoNext);

  Installing := WizardForm.InstallingPage;
  HideChildren(Installing);
  W := Installing.Width;
  LeftEdge := ScaleX(25);
  ContentWidth := W - 2 * LeftEdge;
  AddText(Installing, '正在安装…', 0, ScaleY(68), W, 22, TitleColor, True, True);
  ProgressStatus := AddText(Installing, '正在复制文件，请稍候…', 0, ScaleY(116), W, 11, SecondaryColor, True, False);
  ProgressTrack := AddBar(Installing, TrackColor, LeftEdge, ScaleY(173), ContentWidth, ScaleY(12));
  ProgressFill := AddBar(ProgressTrack, Orange, 0, 0, 0, ProgressTrack.Height);
  AddText(Installing, '安装进度', LeftEdge, ScaleY(200), ScaleX(100), 10, SecondaryColor, False, False);
  PercentLabel := AddText(Installing, '0%', W - LeftEdge - ScaleX(48), ScaleY(200), ScaleX(48), 10, TitleColor, False, True);
  PercentLabel.Alignment := taRightJustify;

  Finished := WizardForm.FinishedPage;
  HideChildren(Finished);
  WizardForm.FinishedPage.Color := clWhite;
  W := Finished.Width;
  AddText(Finished, '✓', 0, ScaleY(53), W, 38, $49A83A, True, True);
  AddText(Finished, '安装完成', 0, ScaleY(134), W, 22, TitleColor, True, True);
  AddText(Finished, '专注时光已成功安装到电脑', 0, ScaleY(180), W, 11, SecondaryColor, True, False);
  LaunchCheck := TNewCheckBox.Create(WizardForm);
  LaunchCheck.Parent := Finished;
  LaunchCheck.Caption := '立即启动 专注时光';
  LaunchCheck.Checked := True;
  LaunchButtonWidth := ScaleX(155);
  LaunchCheck.SetBounds((W - LaunchButtonWidth) div 2, ScaleY(222), LaunchButtonWidth, ScaleY(25));
  DonePrimary := AddPrimary(Finished, '完成', ScaleX(38), Finished.Height - ScaleY(68),
    W - ScaleX(76), @GoNext);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = SettingsPage.ID) and not CustomInstall;
end;

function NextButtonClick(PageID: Integer): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if PageID = SettingsPage.ID then Result := SelectInstallDirectory(DirectoryEdit.Text);
  if (PageID = wpWelcome) and not CustomInstall then Result := SelectInstallDirectory(WizardForm.DirEdit.Text);
  if (PageID = wpFinished) and LaunchCheck.Checked and not WizardSilent then
    if not ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + AddQuotes(ExpandConstant('{#ServiceDirectory}\Launch-FocusApp.ps1')) +
      ' -InstallDirectory ' + AddQuotes(ExpandConstant('{app}')),
      ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      MsgBox('请从开始菜单启动专注时光。', mbInformation, MB_OK);
end;

procedure CurPageChanged(PageID: Integer);
var
  CustomButtons: Boolean;
begin
  if WizardSilent then Exit;
  CustomButtons := (PageID = wpWelcome) or (PageID = SettingsPage.ID) or
    (PageID = wpInstalling) or (PageID = wpFinished);
  if CustomButtons then
  begin
    WizardForm.BackButton.Hide;
    WizardForm.NextButton.Hide;
    if PageID <> wpInstalling then WizardForm.CancelButton.Hide;
    if PageID = wpWelcome then WizardForm.ActiveControl := WelcomePrimary;
    if PageID = SettingsPage.ID then WizardForm.ActiveControl := DirectoryEdit;
    if PageID = wpFinished then WizardForm.ActiveControl := DonePrimary;
  end;
end;

function CreateDesktopShortcut: Boolean;
begin
  Result := not CustomInstall or DesktopCheck.Checked;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
begin
  if (MaxProgress <= 0) or (ProgressTrack = nil) then Exit;
  Percent := (Int64(CurProgress) * 100) div MaxProgress;
  if Percent > 100 then Percent := 100;
  ProgressFill.Width := (ProgressTrack.Width * Percent) div 100;
  PercentLabel.Caption := IntToStr(Percent) + '%';
end;

procedure ShowServiceProgress;
begin
  ProgressStatus.Caption := '正在配置并检查 Windows 服务…';
  ProgressStatus.Update;
  PercentLabel.Hide;
end;

procedure ShowClientProgress;
begin
  ProgressStatus.Caption := '正在复制文件，请稍候…';
  PercentLabel.Show;
end;
