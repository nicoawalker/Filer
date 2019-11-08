#pragma once

#include <string>
#include <filesystem>

#include <msclr/marshal.h>
#include <msclr/marshal_cppstd.h>

#using "WindowsBase.dll"

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Windows;

namespace UnmanagedInterface 
{

	public ref class Displays
	{

	public:

		//returns the dimensions of the monitor under the cursor
		static System::Windows::Point GetMonitorSizeFromPoint( System::Windows::Point point );

		//returns the bounds of the monitor under the cursor
		static System::Windows::Rect GetMonitorRectFromPoint( System::Windows::Point point );

		//returns the bounds of the work rect of the monitor under the cursor. this space does not include the taskbar
		static System::Windows::Rect GetMonitorWorkRectFromPoint( System::Windows::Point point );

		static System::Windows::Point CursorPosition();

	};

	public ref class FileSystem
	{
	public:

		ref class CancellationToken
		{
		public:

			bool cancel;

			CancellationToken()
			{
				cancel = false;
			}
		};

	public:

		/*given a root directory, recursively searches for all subfiles and folders
		returns: Tuple containing the results. Item1: folders, Item2: files*/
		static Tuple<List<String^>^, List<String^>^>^ RecurseDirectoryTree( String^ rootDirectory, CancellationToken^ cancellationToken  );

		/*given a directory, searches for all subfiles and folders but does not enter subdirectories
		returns: Tuple containing the results. Item1: folders, Item2: files*/
		static Tuple<List<String^>^, List<String^>^>^ GetDirectoryContents( String^ directory );

	};
}
