﻿''''' DiskStream.vb
''''' Stream implementation for direct access to raw disk data.
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
Imports System.IO
Imports System.Runtime.Versioning
Imports Arsenal.ImageMounter.IO
Imports Microsoft.Win32.SafeHandles

''' <summary>
''' A FileStream derived class that represents disk devices by overriding properties and methods
''' where FileStream base implementation rely on file API not directly compatible with disk device
''' objects.
''' </summary>
Public Class DiskStream
    Inherits AligningStream

    ''' <summary>
    ''' Initializes an DiskStream object for an open disk device.
    ''' </summary>
    ''' <param name="SafeFileHandle">Open file handle for disk device.</param>
    ''' <param name="AccessMode">Access to request for stream.</param>
    Protected Friend Sub New(SafeFileHandle As SafeFileHandle, AccessMode As FileAccess)
        MyBase.New(New FileStream(SafeFileHandle, AccessMode, bufferSize:=1),
                   Alignment:=If(NativeStruct.GetDiskGeometry(SafeFileHandle)?.BytesPerSector, 512),
                   ownsBaseStream:=True)
    End Sub

    Private _CachedLength As Long?

    ''' <summary>
    ''' Initializes an DiskStream object for an open disk device.
    ''' </summary>
    ''' <param name="SafeFileHandle">Open file handle for disk device.</param>
    ''' <param name="AccessMode">Access to request for stream.</param>
    ''' <param name="DiskSize">Size that should be returned by Length property</param>
    Protected Friend Sub New(SafeFileHandle As SafeFileHandle, AccessMode As FileAccess, DiskSize As Long)
        MyBase.New(New FileStream(SafeFileHandle, AccessMode, bufferSize:=1),
                   Alignment:=If(NativeStruct.GetDiskGeometry(SafeFileHandle)?.BytesPerSector, 512),
                   ownsBaseStream:=True)

        _CachedLength = DiskSize
    End Sub

    Public ReadOnly Property SafeFileHandle As SafeFileHandle
        Get
            Return DirectCast(BaseStream, FileStream).SafeFileHandle
        End Get
    End Property

    ''' <summary>
    ''' Retrieves raw disk size.
    ''' </summary>
    Public Overrides ReadOnly Property Length As Long
        Get
            _CachedLength = If(_CachedLength, NativeStruct.GetDiskSize(SafeFileHandle))

            Return _CachedLength.Value
        End Get
    End Property

    Private _size_from_vbr As Boolean

    Public Property SizeFromVBR As Boolean
        Get
            Return _size_from_vbr
        End Get
        Set
            If Value Then
                _CachedLength = GetVBRPartitionLength()
                If Not _CachedLength.HasValue Then
                    Throw New NotSupportedException
                End If
            Else
                _CachedLength = NativeStruct.GetDiskSize(SafeFileHandle)
                If Not _CachedLength.HasValue Then
                    Throw New NotSupportedException
                End If
            End If
            _size_from_vbr = Value
        End Set
    End Property

    ''' <summary>
    ''' Not implemented.
    ''' </summary>
    Public Overrides Sub SetLength(value As Long)
        Throw New NotImplementedException
    End Sub

    ''' <summary>
    ''' Get partition length as indicated by VBR. Valid for volumes with formatted file system.
    ''' </summary>
    Public Function GetVBRPartitionLength() As Long?

        Dim bytesPerSector = NativeStruct.GetDiskGeometry(SafeFileHandle).Value.BytesPerSector
        Dim vbr = ArrayPool(Of Byte).Shared.Rent(bytesPerSector)
        Try
            Position = 0

            If Read(vbr, 0, bytesPerSector) < bytesPerSector Then
                Return Nothing
            End If

            Dim vbr_sector_size = BitConverter.ToInt16(vbr, &HB)

            If vbr_sector_size <= 0 Then
                Return Nothing
            End If

            Dim total_sectors As Long

            total_sectors = BitConverter.ToUInt16(vbr, &H13)

            If total_sectors = 0 Then

                total_sectors = BitConverter.ToUInt32(vbr, &H20)

            End If

            If total_sectors = 0 Then

                total_sectors = BitConverter.ToInt64(vbr, &H28)

            End If

            If total_sectors < 0 Then

                Return Nothing

            End If

            Return total_sectors * vbr_sector_size

        Finally
            ArrayPool(Of Byte).Shared.Return(vbr)

        End Try

    End Function

End Class



