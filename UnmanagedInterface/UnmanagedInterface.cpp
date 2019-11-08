#include "stdafx.h"
#include <Windows.h>
#include <algorithm>

#include "UnmanagedInterface.h"

#pragma comment(lib, "User32.lib")

namespace UnmanagedInterface
{

	struct MonitorEnumResult
	{
		HMONITOR handle;
		RECT bounds;
	};

	BOOL CALLBACK MonitorEnumCallback( HMONITOR monitorHandle, HDC deviceContext, LPRECT monitorBounds, LPARAM lParam )
	{
		MonitorEnumResult* result = reinterpret_cast<MonitorEnumResult*>( lParam );

		if( monitorHandle == result->handle )
		{
			result->bounds.left = monitorBounds->left;
			result->bounds.right = monitorBounds->right;
			result->bounds.top = monitorBounds->top;
			result->bounds.bottom = monitorBounds->bottom;
		}

		return TRUE;
	}

	
	System::Windows::Point Displays::GetMonitorSizeFromPoint( System::Windows::Point point )
	{
		POINT p;
		p.x = (int)point.X;
		p.y = (int)point.Y;
		HMONITOR monitor = MonitorFromPoint( p, MONITOR_DEFAULTTONEAREST );
		if( monitor == NULL )
		{
			return System::Windows::Point();
		}

		MONITORINFO info;
		ZeroMemory( &info, sizeof( MONITORINFO ) );
		info.cbSize = sizeof( MONITORINFO );

		BOOL result = GetMonitorInfo( monitor, &info );
		if( result == FALSE )
		{
			return System::Windows::Point();
		}

		double width = (double)std::abs( (std::max)( info.rcMonitor.left, info.rcMonitor.right ) - (std::min)( info.rcMonitor.left, info.rcMonitor.right ) );
		double height = (double)std::abs( (std::max)( info.rcMonitor.bottom, info.rcMonitor.top ) - (std::min)( info.rcMonitor.bottom, info.rcMonitor.top ) );

		return System::Windows::Point( width, height );
	}

	System::Windows::Rect Displays::GetMonitorRectFromPoint( System::Windows::Point point )
	{
		POINT p;
		p.x = (int)point.X;
		p.y = (int)point.Y;
		HMONITOR monitor = MonitorFromPoint( p, MONITOR_DEFAULTTONEAREST );
		if( monitor == NULL )
		{
			return System::Windows::Rect();
		}

		MonitorEnumResult* result = new MonitorEnumResult();
		result->handle = monitor;

		EnumDisplayMonitors( NULL, NULL, &MonitorEnumCallback, reinterpret_cast<LPARAM>( result ) );

		//result now contains the bounds of the desired monitor, or an empty rectangle if the monitor wasn't enumerated
		double width = (double)std::abs( ( std::max )( result->bounds.left, result->bounds.right ) - ( std::min )( result->bounds.left, result->bounds.right ) );
		double height = (double)std::abs( ( std::max )( result->bounds.bottom, result->bounds.top ) - ( std::min )( result->bounds.bottom, result->bounds.top ) );

		//return System::Windows::Rect( 0, 0, 0, 0 );
		return System::Windows::Rect( result->bounds.left, result->bounds.top, width, height );

		/*MONITORINFO info;
		ZeroMemory( &info, sizeof( MONITORINFO ) );
		info.cbSize = sizeof( MONITORINFO );

		BOOL result = GetMonitorInfo( monitor, &info );
		if( result == FALSE )
		{
			return System::Windows::Rect();
		}

		double width = (double)std::abs( ( std::max )( info.rcMonitor.left, info.rcMonitor.right ) - ( std::min )( info.rcMonitor.left, info.rcMonitor.right ) );
		double height = (double)std::abs( ( std::max )( info.rcMonitor.bottom, info.rcMonitor.top ) - ( std::min )( info.rcMonitor.bottom, info.rcMonitor.top ) );

		return System::Windows::Rect( info.rcMonitor.left, info.rcMonitor.top, width, height );*/
	}

	System::Windows::Rect Displays::GetMonitorWorkRectFromPoint( System::Windows::Point point )
	{
		POINT p;
		p.x = (int)point.X;
		p.y = (int)point.Y;
		HMONITOR monitor = MonitorFromPoint( p, MONITOR_DEFAULTTONEAREST );
		if( monitor == NULL )
		{
			return System::Windows::Rect();
		}

		MONITORINFO info;
		ZeroMemory( &info, sizeof( MONITORINFO ) );
		info.cbSize = sizeof( MONITORINFO );

		BOOL result = GetMonitorInfo( monitor, &info );
		if( result == FALSE )
		{
			return System::Windows::Rect();
		}

		double width = (double)std::abs( ( std::max )( info.rcWork.left, info.rcWork.right ) - ( std::min )( info.rcWork.left, info.rcWork.right ) );
		double height = (double)std::abs( ( std::max )( info.rcWork.bottom, info.rcWork.top ) - ( std::min )( info.rcWork.bottom, info.rcWork.top ) );

		return System::Windows::Rect( info.rcWork.left, info.rcWork.top, width, height );
	}

	//returns the current position of the mouse
	System::Windows::Point Displays::CursorPosition()
	{
		POINT p;
		if( GetCursorPos( &p ) == FALSE )
		{
			return System::Windows::Point();
		}

		return System::Windows::Point( p.x, p.y );
	}

	Tuple<List<String^>^, List<String^>^>^ FileSystem::RecurseDirectoryTree( String^ rootDirectory, CancellationToken^ cancellationToken )
	{
		std::wstring root = msclr::interop::marshal_as<std::wstring>( rootDirectory );

		List<String^>^ directories = gcnew List<String^>();
		List<String^>^ files = gcnew List<String^>();

		for( const auto& entry : std::experimental::filesystem::recursive_directory_iterator( root ) )
		{
			String^ path = msclr::interop::marshal_as<String^>( entry.path().wstring() );

			path = path->Replace( L"/", L"\\" );

			if( std::experimental::filesystem::is_regular_file( entry ) )
			{
				files->Add( path->ToLower() );

			} else if( std::experimental::filesystem::is_directory( entry ) )
			{
				directories->Add( path->ToLower() );
			}

			if( cancellationToken->cancel ) break;
		}

		return gcnew Tuple<List<String^>^, List<String^>^>( directories, files );
	}

	Tuple<List<String^>^, List<String^>^>^ FileSystem::GetDirectoryContents( String^ directory )
	{
		std::wstring root = msclr::interop::marshal_as<std::wstring>( directory );

		List<String^>^ directories = gcnew List<String^>();
		List<String^>^ files = gcnew List<String^>();

		for( const auto& entry : std::experimental::filesystem::directory_iterator( root ) )
		{
			String^ path = msclr::interop::marshal_as<String^>( entry.path().wstring() );

			path = path->Replace( L"/", L"\\" );

			if( std::experimental::filesystem::is_regular_file( entry ) )
			{
				files->Add( path->ToLower() );

			} else if( std::experimental::filesystem::is_directory( entry ) )
			{
				directories->Add( path->ToLower() );
			}
		}

		return gcnew Tuple<List<String^>^, List<String^>^>( directories, files );
	}

}