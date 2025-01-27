﻿''''' DiskDevice.vb
''''' Class for controlling Arsenal Image Mounter Disk Devices.
''''' 
''''' Copyright (c) 2012-2022, Arsenal Consulting, Inc. (d/b/a Arsenal Recon) <http://www.ArsenalRecon.com>
''''' This source code and API are available under the terms of the Affero General Public
''''' License v3.
'''''
''''' Please see LICENSE.txt for full license terms, including the availability of
''''' proprietary exceptions.
''''' Questions, comments, or requests for clarification: http://ArsenalRecon.com/contact/
'''''

Imports System.Buffers
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Runtime.Versioning
Imports System.Threading
Imports Arsenal.ImageMounter.IO
Imports Microsoft.Win32.SafeHandles

''' <summary>
''' Represents disk objects, attached to a virtual or physical SCSI adapter.
''' </summary>
Public Class DiskDevice
    Inherits DeviceObject

    Private _RawDiskStream As DiskStream

    Private _CachedAddress As SCSI_ADDRESS?

    ''' <summary>
    ''' Returns the device path used to open this device object, if opened by name.
    ''' If the object was opened in any other way, such as by supplying an already
    ''' open handle, this property returns null/Nothing.
    ''' </summary>
    Public ReadOnly Property DevicePath As ReadOnlyMemory(Of Char)

    Private Sub AllowExtendedDasdIo()
        If Not RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
            Return
        End If
        If Not NativeFileIO.UnsafeNativeMethods.DeviceIoControl(SafeFileHandle, NativeConstants.FSCTL_ALLOW_EXTENDED_DASD_IO, IntPtr.Zero, 0UI, IntPtr.Zero, 0UI, 0UI, IntPtr.Zero) Then
            Dim errcode = Marshal.GetLastWin32Error()
            If errcode <> NativeConstants.ERROR_INVALID_PARAMETER AndAlso
                errcode <> NativeConstants.ERROR_INVALID_FUNCTION Then

                Trace.WriteLine($"FSCTL_ALLOW_EXTENDED_DASD_IO failed for '{_DevicePath}': {errcode}")
            End If
        End If
    End Sub

    Protected Friend Sub New(DeviceNameAndHandle As KeyValuePair(Of ReadOnlyMemory(Of Char), SafeFileHandle), AccessMode As FileAccess)
        MyBase.New(DeviceNameAndHandle.Value, AccessMode)

        _DevicePath = DeviceNameAndHandle.Key

        AllowExtendedDasdIo()
    End Sub

    ''' <summary>
    ''' Opens an disk device object without requesting read or write permissions. The
    ''' resulting object can only be used to query properties like SCSI address, disk
    ''' size and similar, but not for reading or writing raw disk data.
    ''' </summary>
    ''' <param name="DevicePath"></param>
    Public Sub New(DevicePath As ReadOnlyMemory(Of Char))
        MyBase.New(DevicePath)

        _DevicePath = DevicePath

        AllowExtendedDasdIo()
    End Sub

    ''' <summary>
    ''' Opens an disk device object, requesting read, write or both permissions.
    ''' </summary>
    ''' <param name="DevicePath"></param>
    ''' <param name="AccessMode"></param>
    Public Sub New(DevicePath As ReadOnlyMemory(Of Char), AccessMode As FileAccess)
        MyBase.New(DevicePath, AccessMode)

        _DevicePath = DevicePath

        AllowExtendedDasdIo()
    End Sub

    ''' <summary>
    ''' Opens an disk device object without requesting read or write permissions. The
    ''' resulting object can only be used to query properties like SCSI address, disk
    ''' size and similar, but not for reading or writing raw disk data.
    ''' </summary>
    ''' <param name="DevicePath"></param>
    Public Sub New(DevicePath As String)
        Me.New(DevicePath.AsMemory())

    End Sub

    ''' <summary>
    ''' Opens an disk device object, requesting read, write or both permissions.
    ''' </summary>
    ''' <param name="DevicePath"></param>
    ''' <param name="AccessMode"></param>
    Public Sub New(DevicePath As String, AccessMode As FileAccess)
        Me.New(DevicePath.AsMemory(), AccessMode)

    End Sub

    ''' <summary>
    ''' Opens an disk device object.
    ''' </summary>
    ''' <param name="ScsiAddress"></param>
    ''' <param name="AccessMode"></param>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub New(ScsiAddress As SCSI_ADDRESS, AccessMode As FileAccess)
        Me.New(NativeFileIO.OpenDiskByScsiAddress(ScsiAddress, AccessMode), AccessMode)

    End Sub

    ''' <summary>
    ''' Retrieves device number for this disk on the owner SCSI adapter.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property DeviceNumber As UInteger
        Get
            If _CachedAddress Is Nothing Then
                Dim scsi_address = ScsiAddress.Value

                Using driver As New ScsiAdapter(scsi_address.PortNumber)
                End Using

                _CachedAddress = scsi_address
            End If

            Return _CachedAddress.Value.DWordDeviceNumber

        End Get
    End Property

    ''' <summary>
    ''' Retrieves SCSI address for this disk.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property ScsiAddress As SCSI_ADDRESS?
        Get
            Return NativeFileIO.GetScsiAddress(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Retrieves storage device type and physical disk number information.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property StorageDeviceNumber As STORAGE_DEVICE_NUMBER?
        Get
            Return NativeFileIO.GetStorageDeviceNumber(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Retrieves StorageStandardProperties information.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property StorageStandardProperties As StorageStandardProperties?
        Get
            Return NativeFileIO.GetStorageStandardProperties(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Retrieves TRIM enabled information.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property TrimEnabled As Boolean?
        Get
            Return NativeFileIO.GetStorageTrimProperties(SafeFileHandle)
        End Get
    End Property

    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub TrimRange(startingOffset As Long, lengthInBytes As ULong)
        NativeCalls.TrimDiskRange(SafeFileHandle, startingOffset, lengthInBytes)
    End Sub

    ''' <summary>
    ''' Enumerates disk volumes that use extents of this disk.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Function EnumerateDiskVolumes() As IEnumerable(Of String)

        Dim disk_number = StorageDeviceNumber

        If Not disk_number.HasValue Then
            Return Nothing
        End If

        Trace.WriteLine($"Found disk number: {disk_number.Value.DeviceNumber}")

        Return NativeFileIO.EnumerateDiskVolumes(disk_number.Value.DeviceNumber)

    End Function

    ''' <summary>
    ''' Opens SCSI adapter that created this virtual disk.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Function OpenAdapter() As ScsiAdapter

        Return New ScsiAdapter(NativeFileIO.GetScsiAddress(SafeFileHandle).Value.PortNumber)

    End Function

    ''' <summary>
    ''' Updates disk properties by re-enumerating partition table.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub UpdateProperties()

        NativeFileIO.UpdateDiskProperties(SafeFileHandle, throwOnFailure:=True)

    End Sub

    ''' <summary>
    ''' Retrieves the physical location of a specified volume on one or more disks. 
    ''' </summary>
    ''' <returns></returns>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Function GetVolumeDiskExtents() As DiskExtent()

        Return NativeFileIO.GetVolumeDiskExtents(SafeFileHandle)

    End Function

    ''' <summary>
    ''' Gets or sets disk signature stored in boot record.
    ''' </summary>
    Public Property DiskSignature As UInteger?
        Get
            Dim bytesPerSector = Geometry.Value.BytesPerSector
            Dim rawsig = ArrayPool(Of Byte).Shared.Rent(bytesPerSector)
            Try
                With GetRawDiskStream()
                    .Position = 0
                    .Read(rawsig, 0, bytesPerSector)
                End With
                If BitConverter.ToUInt16(rawsig, &H1FE) = &HAA55US AndAlso
                    rawsig(&H1C2) <> &HEE AndAlso
                    (rawsig(&H1BE) And &H7F) = 0 AndAlso
                    (rawsig(&H1CE) And &H7F) = 0 AndAlso
                    (rawsig(&H1DE) And &H7F) = 0 AndAlso
                    (rawsig(&H1EE) And &H7F) = 0 Then

                    Return BitConverter.ToUInt32(rawsig, &H1B8)
                End If

            Finally
                ArrayPool(Of Byte).Shared.Return(rawsig)

            End Try

            Return Nothing
        End Get
        Set
            If Not Value.HasValue Then
                Return
            End If

            Dim bytesPerSector = Geometry.Value.BytesPerSector
            Dim rawsig = ArrayPool(Of Byte).Shared.Rent(bytesPerSector)
            Try
                With GetRawDiskStream()
                    .Position = 0
                    .Read(rawsig, 0, bytesPerSector)
                    MemoryMarshal.Write(rawsig.AsSpan(&H1B8), Value.Value)
                    .Position = 0
                    .Write(rawsig, 0, bytesPerSector)
                End With

            Finally
                ArrayPool(Of Byte).Shared.Return(rawsig)

            End Try
        End Set
    End Property

    ''' <summary>
    ''' Gets or sets disk signature stored in boot record.
    ''' </summary>
    Public Property VBRHiddenSectorsCount As UInteger?
        Get
            Dim bytesPerSector = Geometry.Value.BytesPerSector
            Dim rawsig = ArrayPool(Of Byte).Shared.Rent(bytesPerSector)
            Try
                With GetRawDiskStream()
                    .Position = 0
                    .Read(rawsig, 0, bytesPerSector)
                End With

                If BitConverter.ToUInt16(rawsig, &H1FE) = &HAA55US Then
                    Return BitConverter.ToUInt32(rawsig, &H1C)
                End If

            Finally
                ArrayPool(Of Byte).Shared.Return(rawsig)

            End Try

            Return Nothing
        End Get
        Set
            If Not Value.HasValue Then
                Return
            End If

            Dim bytesPerSector = Geometry.Value.BytesPerSector
            Dim rawsig = ArrayPool(Of Byte).Shared.Rent(bytesPerSector)
            Try
                With GetRawDiskStream()
                    .Position = 0
                    .Read(rawsig, 0, bytesPerSector)
                    MemoryMarshal.Write(rawsig.AsSpan(&H1C), Value.Value)
                    .Position = 0
                    .Write(rawsig, 0, bytesPerSector)
                End With

            Finally
                ArrayPool(Of Byte).Shared.Return(rawsig)

            End Try
        End Set
    End Property

    ''' <summary>
    ''' Reads first sector of disk or disk volume
    ''' </summary>
    Public Function ReadBootSector() As Byte()

        Dim bootsect(0 To Geometry.Value.BytesPerSector - 1) As Byte
        Dim bytesread As Integer

        With GetRawDiskStream()
            .Position = 0
            bytesread = .Read(bootsect, 0, bootsect.Length)
        End With

        If bytesread < 512 Then
            Return Nothing
        End If

        If bytesread <> bootsect.Length Then
            Array.Resize(bootsect, bytesread)
        End If

        Return bootsect

    End Function

    ''' <summary>
    ''' Return a value indicating whether present sector 0 data indicates a valid MBR
    ''' with a partition table.
    ''' </summary>
    Public ReadOnly Property HasValidPartitionTable As Boolean
        Get

            Dim bootsect = ReadBootSector()

            If bootsect Is Nothing Then
                Return False
            End If

            Return BitConverter.ToUInt16(bootsect, &H1FE) = &HAA55US AndAlso
                (bootsect(&H1BE) And &H7F) = 0 AndAlso
                (bootsect(&H1CE) And &H7F) = 0 AndAlso
                (bootsect(&H1DE) And &H7F) = 0 AndAlso
                (bootsect(&H1EE) And &H7F) = 0

        End Get
    End Property

    ''' <summary>
    ''' Return a value indicating whether present sector 0 data indicates a valid MBR
    ''' with a partition table and not blank or fake boot code.
    ''' </summary>
    Public ReadOnly Property HasValidBootCode As Boolean
        Get

            Dim bootsect = ReadBootSector()

            If bootsect Is Nothing OrElse
                bootsect(0) = 0 OrElse
                bootsect.AsSpan(0, NativeConstants.DefaultBootCode.Length).
                SequenceEqual(NativeConstants.DefaultBootCode.Span) Then

                Return False

            End If

            Return BitConverter.ToUInt16(bootsect, &H1FE) = &HAA55US AndAlso
                (bootsect(&H1BE) And &H7F) = 0 AndAlso
                (bootsect(&H1CE) And &H7F) = 0 AndAlso
                (bootsect(&H1DE) And &H7F) = 0 AndAlso
                (bootsect(&H1EE) And &H7F) = 0

        End Get
    End Property

    ''' <summary>
    ''' Flush buffers for a disk or volume.
    ''' </summary>
    Public Sub FlushBuffers()
        If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
            If _RawDiskStream IsNot Nothing Then
                _RawDiskStream.Flush()
            Else
                NativeFileIO.FlushBuffers(SafeFileHandle)
            End If
        Else
            GetRawDiskStream().Flush()
        End If
    End Sub

    ''' <summary>
    ''' Gets or sets physical disk offline attribute. Only valid for
    ''' physical disk objects, not volumes or partitions.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Property DiskPolicyOffline As Boolean?
        Get
            Return NativeFileIO.GetDiskOffline(SafeFileHandle)
        End Get
        Set
            If Value.HasValue Then
                NativeFileIO.SetDiskOffline(SafeFileHandle, Value.Value)
            End If
        End Set
    End Property

    ''' <summary>
    ''' Gets or sets physical disk read only attribute. Only valid for
    ''' physical disk objects, not volumes or partitions.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Property DiskPolicyReadOnly As Boolean?
        Get
            Return NativeFileIO.GetDiskReadOnly(SafeFileHandle)
        End Get
        Set
            If Value.HasValue Then
                NativeFileIO.SetDiskReadOnly(SafeFileHandle, Value.Value)
            End If
        End Set
    End Property

    ''' <summary>
    ''' Sets disk volume offline attribute. Only valid for logical
    ''' disk volumes, not physical disk drives.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub SetVolumeOffline(value As Boolean)

        NativeFileIO.SetVolumeOffline(SafeFileHandle, value)

    End Sub

    ''' <summary>
    ''' Gets information about a partition stored on a disk with MBR
    ''' partition layout. This property is not available for physical
    ''' disks, only disk partitions are supported.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property PartitionInformation As PARTITION_INFORMATION?
        Get
            Return NativeFileIO.GetPartitionInformation(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Gets information about a disk partition. This property is not
    ''' available for physical disks, only disk partitions are supported.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property PartitionInformationEx As PARTITION_INFORMATION_EX?
        Get
            Return NativeFileIO.GetPartitionInformationEx(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Gets information about a disk partitions. This property is available
    ''' for physical disks, not disk partitions.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Property DriveLayoutEx As NativeFileIO.DriveLayoutInformation
        Get
            Return NativeFileIO.GetDriveLayoutEx(SafeFileHandle)
        End Get
        Set
            NativeFileIO.SetDriveLayoutEx(SafeFileHandle, Value)
        End Set
    End Property

    ''' <summary>
    ''' Initialize a raw disk device for use with Windows. This method is available
    ''' for physical disks, not disk partitions.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub InitializeDisk(PartitionStyle As PARTITION_STYLE)
        NativeCalls.InitializeDisk(SafeFileHandle, PartitionStyle)
    End Sub

    ''' <summary>
    ''' Disk identifier string.
    ''' </summary>
    ''' <returns>8 digit hex string for MBR disks or disk GUID for
    ''' GPT disks.</returns>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property DiskId As String
        Get
            Return If(DriveLayoutEx?.ToString(), "(Unknown)")
        End Get
    End Property

    ''' <summary>
    ''' Retrieves properties for an existing virtual disk.
    ''' </summary>
    ''' <param name="DeviceNumber">Device number of virtual disk.</param>
    ''' <param name="DiskSize">Size of virtual disk.</param>
    ''' <param name="BytesPerSector">Number of bytes per sector for virtual disk geometry.</param>
    ''' <param name="ImageOffset">A skip offset if virtual disk data does not begin immediately at start of disk image file.
    ''' Frequently used with image formats like Nero NRG which start with a file header not used by Arsenal Image Mounter or Windows
    ''' filesystem drivers.</param>
    ''' <param name="Flags">Flags specifying properties for virtual disk. See comments for each flag value.</param>
    ''' <param name="Filename">Name of disk image file holding storage for file type virtual disk or used to create a
    ''' virtual memory type virtual disk.</param>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub QueryDevice(<Out> ByRef DeviceNumber As UInteger,
                           <Out> ByRef DiskSize As Long,
                           <Out> ByRef BytesPerSector As UInteger,
                           <Out> ByRef ImageOffset As Long,
                           <Out> ByRef Flags As DeviceFlags,
                           <Out> ByRef Filename As String)

        Dim scsi_address = ScsiAddress.Value

        Using adapter As New ScsiAdapter(scsi_address.PortNumber)

            DeviceNumber = scsi_address.DWordDeviceNumber

            adapter.QueryDevice(DeviceNumber,
                                DiskSize,
                                BytesPerSector,
                                ImageOffset,
                                Flags,
                                Filename)

        End Using

    End Sub

    ''' <summary>
    ''' Retrieves properties for an existing virtual disk.
    ''' </summary>
    ''' <param name="DeviceNumber">Device number of virtual disk.</param>
    ''' <param name="DiskSize">Size of virtual disk.</param>
    ''' <param name="BytesPerSector">Number of bytes per sector for virtual disk geometry.</param>
    ''' <param name="ImageOffset">A skip offset if virtual disk data does not begin immediately at start of disk image file.
    ''' Frequently used with image formats like Nero NRG which start with a file header not used by Arsenal Image Mounter or Windows
    ''' filesystem drivers.</param>
    ''' <param name="Flags">Flags specifying properties for virtual disk. See comments for each flag value.</param>
    ''' <param name="Filename">Name of disk image file holding storage for file type virtual disk or used to create a
    ''' virtual memory type virtual disk.</param>
    ''' <param name="WriteOverlayImagefile">Path to differencing file used in write-temporary mode.</param>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub QueryDevice(<Out> ByRef DeviceNumber As UInteger,
                           <Out> ByRef DiskSize As Long,
                           <Out> ByRef BytesPerSector As UInteger,
                           <Out> ByRef ImageOffset As Long,
                           <Out> ByRef Flags As DeviceFlags,
                           <Out> ByRef Filename As String,
                           <Out> ByRef WriteOverlayImagefile As String)

        Dim scsi_address = ScsiAddress.Value

        Using adapter As New ScsiAdapter(scsi_address.PortNumber)

            DeviceNumber = scsi_address.DWordDeviceNumber

            adapter.QueryDevice(DeviceNumber,
                                DiskSize,
                                BytesPerSector,
                                ImageOffset,
                                Flags,
                                Filename,
                                WriteOverlayImagefile)

        End Using

    End Sub

    ''' <summary>
    ''' Retrieves properties for an existing virtual disk.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Function QueryDevice() As DeviceProperties

        Dim scsi_address = ScsiAddress.Value

        Using adapter As New ScsiAdapter(scsi_address.PortNumber)

            Return adapter.QueryDevice(scsi_address.DWordDeviceNumber)

        End Using

    End Function

    ''' <summary>
    ''' Removes this virtual disk from adapter.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub RemoveDevice()

        Dim scsi_address = ScsiAddress.Value

        Using adapter As New ScsiAdapter(scsi_address.PortNumber)

            adapter.RemoveDevice(scsi_address.DWordDeviceNumber)

        End Using

    End Sub

    ''' <summary>
    ''' Retrieves volume size of disk device.
    ''' </summary>
    Public ReadOnly Property DiskSize As Long?
        Get
            Return NativeStruct.GetDiskSize(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Retrieves partition information.
    ''' </summary>
    ''' <returns></returns>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property VolumeSizeInformation As FILE_FS_FULL_SIZE_INFORMATION?
        Get
            Return NativeCalls.GetVolumeSizeInformation(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Determines whether disk is writable or read-only.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property IsDiskWritable As Boolean
        Get
            Return NativeFileIO.IsDiskWritable(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Returns logical disk geometry. Normally, only the BytesPerSector member
    ''' contains data of interest.
    ''' </summary>
    Public ReadOnly Property Geometry As DISK_GEOMETRY?
        Get
            Return NativeStruct.GetDiskGeometry(SafeFileHandle)
        End Get
    End Property

    ''' <summary>
    ''' Locks and dismounts filesystem on a volume. Upon successful return, further access to the device
    ''' can only be done through this device object instance until it is either closed (disposed) or lock is
    ''' released on the underlying handle.
    ''' </summary>
    ''' <param name="Force">Indicates if True that volume should be immediately dismounted even if it
    ''' cannot be locked. This causes all open handles to files on the volume to become invalid. If False,
    ''' successful lock (no other open handles) is required before attempting to dismount filesystem.</param>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub DismountVolumeFilesystem(Force As Boolean)

        NativeFileIO.Win32Try(NativeFileIO.DismountVolumeFilesystem(SafeFileHandle, Force))

    End Sub

    ''' <summary>
    ''' Locks and dismounts filesystem on a volume. Upon successful return, further access to the device
    ''' can only be done through this device object instance until it is either closed (disposed) or lock is
    ''' released on the underlying handle.
    ''' </summary>
    ''' <param name="Force">Indicates if True that volume should be immediately dismounted even if it
    ''' cannot be locked. This causes all open handles to files on the volume to become invalid. If False,
    ''' successful lock (no other open handles) is required before attempting to dismount filesystem.</param>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Async Function DismountVolumeFilesystemAsync(Force As Boolean, cancel As CancellationToken) As Task

        NativeFileIO.Win32Try(Await NativeFileIO.DismountVolumeFilesystemAsync(SafeFileHandle, Force, cancel).ConfigureAwait(False))

    End Function

    ''' <summary>
    ''' Get live statistics from write filter driver.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public ReadOnly Property WriteOverlayStatus As WriteFilterStatistics?
        Get
            Dim statistics As WriteFilterStatistics = Nothing

            If API.GetWriteOverlayStatus(SafeFileHandle, statistics) <> NativeConstants.NO_ERROR Then
                Return Nothing
            End If

            Return statistics
        End Get
    End Property

    ''' <summary>
    ''' Deletes the write overlay image file after use. Also sets the filter driver to
    ''' silently ignore flush requests to improve performance when integrity of the write
    ''' overlay image is not needed for future sessions.
    ''' </summary>
    <SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)>
    Public Sub SetWriteOverlayDeleteOnClose()
        Dim rc = API.SetWriteOverlayDeleteOnClose(SafeFileHandle)
        If rc <> NativeConstants.NO_ERROR Then
            Throw New Win32Exception(rc)
        End If
    End Sub

    ''' <summary>
    ''' Returns an DiskStream object that can be used to directly access disk data.
    ''' The returned stream automatically sector-aligns I/O.
    ''' </summary>
    Public Function GetRawDiskStream() As DiskStream

        If _RawDiskStream Is Nothing Then

            _RawDiskStream = New DiskStream(SafeFileHandle,
                                            If(AccessMode = 0, FileAccess.Read, AccessMode))

        End If

        Return _RawDiskStream

    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)

        If disposing Then

            _RawDiskStream?.Dispose()

        End If

        _RawDiskStream = Nothing

        MyBase.Dispose(disposing)

    End Sub

End Class


