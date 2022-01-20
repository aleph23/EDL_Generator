﻿Public Class frm_Main
    Private threadEnd As Boolean = False
    Delegate Sub AddLogD(ByVal strText As String, ByVal InfoLevel As String)
    Delegate Sub SetProgD(ByVal value As Int64)
    Delegate Sub SetProgStyleD(ByVal style As ProgressBarStyle)

    Private Sub LockUnlockCtrl(Optional isUnlock As Boolean = True)
        txt_Disk.Enabled = isUnlock
        txt_firehose.Enabled = isUnlock
        txt_Sector.Enabled = isUnlock
        btn_browseFirehose.Enabled = isUnlock
        btn_Go.Enabled = isUnlock
    End Sub

    Private Sub AddLogInvoke(ByVal strText As String, Optional ByVal InfoLevel As String = "I")
        Me.Invoke(New AddLogD(AddressOf AddLog), strText, InfoLevel)
    End Sub

    Private Sub AddLog(ByVal strText As String, ByVal InfoLevel As String)
        If InfoLevel <> "" Then strText = InfoLevel & " " & strText
        If InfoLevel = "V" And combo_LogLevel.SelectedIndex < 4 Then Exit Sub
        If InfoLevel = "D" And combo_LogLevel.SelectedIndex < 3 Then Exit Sub
        If InfoLevel = "I" And combo_LogLevel.SelectedIndex < 2 Then Exit Sub
        If InfoLevel = "W" And combo_LogLevel.SelectedIndex < 1 Then Exit Sub
        txt_Log.SelectionStart = txt_Log.TextLength
        txt_Log.ScrollToCaret()
        txt_Log.AppendText(strText & vbCrLf)
    End Sub

    Private Sub SetProg(ByVal value As Int64)
        prog.Value = value
    End Sub

    Private Sub SetProgMax(ByVal value As Int64)
        prog.Maximum = value
    End Sub

    Private Sub SetProgStyle(ByVal style As ProgressBarStyle)
        prog.Style = style
    End Sub

    Private Function RunCommand(ByVal strProc As String, ByVal strArgs As String)
        AddLogInvoke("implement " & strProc & " Use parameters " & strArgs, "V")
        Dim cmd_result As String = RunCommandR(strProc, strArgs)
        AddLogInvoke("Result: " & vbCrLf & cmd_result, "V")
        Return cmd_result
    End Function

    Private Sub RunExec()
        Dim savePath As String = dlg_folder.SelectedPath & "\"
        sectorSize = Convert.ToInt64(txt_Sector.Text)
        Dim disk() As String = Split(txt_Disk.Text, ";"), diskNum As Int64
        Me.Invoke(New SetProgStyleD(AddressOf SetProgStyle), ProgressBarStyle.Marquee)
        AddLogInvoke("start adb service...")
        RunCommand(adbExe, "kill-server")
        RunCommand(adbExe, "start-server")
        Me.Invoke(New SetProgStyleD(AddressOf SetProgStyle), ProgressBarStyle.Blocks)
        If InStr(RunCommand(adbExe, "get-state"), "recovery") <= 0 Then
          AddLogInvoke("Please connect the device and reboot to recovery mode and try again ", "E")
            GoTo pEnd
        End If
        If InStr(RunCommand(adbExe, "shell "" ls /sbin/sgdisk || echo 'no sgdisk' """), "no sgdisk") > 0 Then
            AddLogInvoke("Push the sgdisk program to the device...")
            RunCommand(adbExe, "push """ & sgdiskBin & """ /sbin/sgdisk")
            RunCommand(adbExe, "shell ""chmod 0755 /sbin/sgdisk""")
        End If
        Dim writer As New Xml.XmlTextWriter(savePath & "partition.xml", System.Text.Encoding.GetEncoding("utf-8"))
        With writer
            .Formatting = Xml.Formatting.Indented
            .WriteRaw("<?xml version=""1.0"" ?>")
            .WriteStartElement("configuration")
            .WriteStartElement("parser_instructions")
            .WriteRaw(vbCrLf &
                      "    WRITE_PROTECT_BOUNDARY_IN_KB = 0" & vbCrLf &
                      "    SECTOR_SIZE_IN_BYTES = " & sectorSize & vbCrLf &
                      "    GROW_LAST_PARTITION_TO_FILL_DISK= true" & vbCrLf)
            .WriteEndElement()
        End With
        For diskNum = 0 To UBound(disk)
            AddLogInvoke("Read partition information... from " & disk(diskNum))
            Dim g_result() As String
            Dim num_gResult As Int32, tmp_g_result() As String, flagStartAdd As Int64 = 0
            ReDim part(0)
            g_result = Split(RunCommand(adbExe, "shell /sbin/sgdisk --print " & disk(diskNum)), vbCrLf)
            Me.Invoke(New SetProgD(AddressOf SetProgMax), UBound(g_result))
            For num_gResult = 0 To UBound(g_result)
                Me.Invoke(New SetProgD(AddressOf SetProg), num_gResult)
                If InStr(LCase(g_result(num_gResult)), "start (sector)") > 0 Then
                    flagStartAdd = num_gResult + 1
                    ReDim part(UBound(g_result) - flagStartAdd - 2)
                    Continue For
                End If
                If flagStartAdd > 0 And num_gResult <= UBound(g_result) - 2 Then
                    tmp_g_result = Split(ReplaceRepeatSpace(g_result(num_gResult)), " ")
                    With part(num_gResult - flagStartAdd)
                        .start_Sector = Convert.ToInt64(tmp_g_result(2))
                        .end_Sector = Convert.ToInt64(tmp_g_result(3))
                        .Label = tmp_g_result(7)
                        .bootable = selectBootable(.Label)
                        .bakFile = selectBackupName(.Label)
                        If .bakFile = "" Then
                            .backupIt = False
                        Else
                            .backupIt = selectBackup(.Label)
                        End If
                        If .backupIt And (tmp_g_result(6) = "8300" Or tmp_g_result(6) = "0700") Then .sparsed = True
                        .isReadOnly = selectReadOnly(.Label)
                        If .Label <> "last_parti" Then
                            .typeGUID = CutStr(RunCommand(adbExe, "shell /sbin/sgdisk --info=" & num_gResult - flagStartAdd + 1 & " " & disk(diskNum)), "Partition GUID code: ", " (")
                        Else
                            .typeGUID = "00000000-0000-0000-0000-000000000000"
                        End If
                    End With
                    AddLogInvoke("read to: " & tmp_g_result(1) &
                           " ,Label: " & tmp_g_result(7) &
                           " ,type(GUID): " & part(num_gResult - flagStartAdd).typeGUID, "D")
                End If
            Next
            Me.Invoke(New SetProgD(AddressOf SetProg), 0)
            If UBound(part) = 0 And part(0).Label = "" Then
                AddLogInvoke("from disk " & disk(diskNum) & " Failed to read partition information, terminated...", "E")
                GoTo pEnd
            End If
            AddLogInvoke("Waiting for Partition Editing...")
            Me.Invoke(New SetProgStyleD(AddressOf SetProgStyle), ProgressBarStyle.Marquee)
            flagPartConf = True
            frm_EditPartConf.Show()
            Do While flagPartConf
                My.Application.DoEvents()
                Threading.Thread.Sleep(5)
            Loop

            AddLogInvoke("Start backing up partitions... from " & disk(diskNum))
            Me.Invoke(New SetProgStyleD(AddressOf SetProgStyle), ProgressBarStyle.Blocks)
            Me.Invoke(New SetProgD(AddressOf SetProgMax), UBound(part))
            Dim i As Int64
            For i = 0 To UBound(part)
                If Not part(i).backupIt Or part(i).bakFile = "" Or CheckFile(savePath & part(i).bakFile) Then
                    AddLogInvoke("jump over " & part(i).Label, "D")
                    GoTo pEnd1
                End If
                  AddLogInvoke("back up " & part(i).Label & " to file " & part(i).bakFile, "D")
                RunCommand(adbExe,
                           "pull /dev/block/bootdevice/by-name/" & part(i).Label & " """ &
                            savePath & part(i).bakFile & """")
                If Not CheckFile(savePath & part(i).bakFile) Then
                    AddLogInvoke("backup " & part(i).Label & " Fail!", "W")
                Else
                    If part(i).sparsed Then
                                AddLogInvoke("try sparce " & part(i).bakFile, "D")
                        RunCommand(sparseExe,
                                   """" & savePath & part(i).bakFile & """ """ &
                                   savePath & part(i).bakFile & ".sparse.img""")

                        If CheckFile(savePath & part(i).bakFile & ".sparse.img") Then
                            IO.File.Delete(savePath & part(i).bakFile)
                            IO.File.Move(savePath & part(i).bakFile & ".sparse.img",
                                     savePath & part(i).bakFile)
                        Else
                                  AddLogInvoke("Sparce failed " & part(i).bakFile, "W")
                            part(i).sparsed = False
                        End If
                    End If
                End If
                            pEnd1:
                Me.Invoke(New SetProgD(AddressOf SetProg), i)
            Next
            Me.Invoke(New SetProgD(AddressOf SetProg), 0)
               AddLogInvoke("write partition.xml, disk " & diskNum)
            With writer
                .WriteStartElement("physical_partition")
                For i = 0 To UBound(part)
                    .WriteStartElement("partition")
                    .WriteAttributeString("label", part(i).Label)
                    .WriteAttributeString("size_in_kb", (part(i).end_Sector - part(i).start_Sector + 1) * sectorSize \ 1024)
                    .WriteAttributeString("type", part(i).typeGUID)
                    .WriteAttributeString("sparse", part(i).sparsed)
                    .WriteAttributeString("bootable", LCase(part(i).bootable))
                    .WriteAttributeString("readonly", LCase(part(i).isReadOnly))
                    .WriteAttributeString("filename", part(i).bakFile)
                    .WriteEndElement()
                    Me.Invoke(New SetProgD(AddressOf SetProg), i)
                Next
                .WriteEndElement()
            End With
            Me.Invoke(New SetProgD(AddressOf SetProg), 0)
            Me.Invoke(New SetProgStyleD(AddressOf SetProgStyle), ProgressBarStyle.Marquee)
        Next
        With writer
            .WriteFullEndElement()
            .Close()
        End With

        If cSahara.Checked Then
            ' ongoing
            AddLogInvoke("Use the Sahara protocol，Generate msimage file...")

        End If

        AddLogInvoke("Use the Qualcomm solution script to generate the required files...")
        Me.Invoke(New SetProgD(AddressOf SetProgMax), UBound(disk))
        Me.Invoke(New SetProgD(AddressOf SetProg), 0)
        For diskNum = 0 To UBound(disk)
            Me.Invoke(New SetProgD(AddressOf SetProg), diskNum)
            RunCommand(pToolExe, "-x """ & savePath & "partition.xml"" -p " & diskNum & " -t """ & Strings.Left(savePath, savePath.Length - 1) & """")
        Next
        Me.Invoke(New SetProgD(AddressOf SetProg), 0)

        AddLogInvoke("Copy the binaries to the output folder...")
        IO.File.Copy(txt_firehose.Text, savePath & IO.Path.GetFileName(txt_firehose.Text), True)

        Me.Invoke(New SetProgStyleD(AddressOf SetProgStyle), ProgressBarStyle.Blocks)
                    AddLogInvoke("Done!")
pEnd:
        SyncLock Threading.Thread.CurrentThread
            threadEnd = True
        End SyncLock
    End Sub

    Private Sub btn_Go_Click(sender As Object, e As EventArgs) Handles btn_Go.Click
        If Trim(txt_Disk.Text) = "" Then
            txt_Disk.Text = "/dev/block/mmcblk0"
            AddLog("Disk path not specified, using default value!" & vbCrLf, "W")
        End If
        If Trim(txt_Sector.Text) = "" Then
            txt_Sector.Text = "512"
            AddLog("Sector size is not specified, the default value is used!" & vbCrLf, "W")
        End If
        If CheckFile(txt_firehose.Text) = False Then
            AddLog("Could not find binary file, stop!" & vbCrLf, "E")
            MsgBox("binary file not found!", vbCritical + vbSystemModal)
            Exit Sub
        End If
        dlg_folder.ShowDialog()
        If dlg_folder.SelectedPath <> "" Then
            threadEnd = False
            LockUnlockCtrl(False)
            Dim tmpThread As System.Threading.Thread = New System.Threading.Thread(AddressOf RunExec)
            tmpThread.Start()
            Do While Not threadEnd
                My.Application.DoEvents()
                Threading.Thread.Sleep(5)
            Loop
            LockUnlockCtrl()
        End If
        dlg_folder.SelectedPath = ""
    End Sub

    Private Sub btn_browseFirehose_Click(sender As Object, e As EventArgs) Handles btn_browseFirehose.Click
        dlg_open.ShowDialog()
        If dlg_open.FileName <> "" Then txt_firehose.Text = dlg_open.FileName
        dlg_open.FileName = ""
    End Sub

    Private Sub frm_Main_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        combo_LogLevel.SelectedItem = combo_LogLevel.Items(2)
    End Sub
End Class
