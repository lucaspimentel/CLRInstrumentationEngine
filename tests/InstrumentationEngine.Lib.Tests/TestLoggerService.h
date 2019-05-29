#pragma once

#include "DebugLoggerSink.h"
#include "EventLoggerSink.h"
#include "FileLoggerSink.h"
#include "HostLoggerSink.h"
#include "LoggerService.h"

namespace InstrumentationEngineLibTests
{
    class CTestEventLoggerSink :
        public CEventLoggerSink
    {
    private:
        std::vector<tstring> m_entries;

    protected:
        void LogEvent(const tstring& tsEntry) override
        {
            // Sleep some arbitrary amount of time.
            Sleep(m_entries.size() % 25); // ms

            m_entries.push_back(tsEntry);
        }
    };

    class CTestFileLoggerSink :
        public CFileLoggerSink
    {

    private:
        tstring m_tsPath;
        LoggingFlags m_flags;

    public:
        CTestFileLoggerSink(LoggingFlags flags, tstring& tsPath) :
            m_tsPath(tsPath),
            m_flags(flags)
        {
        }

    protected:
        HRESULT GetPathAndFlags(_Out_ tstring* ptsPath, _Out_ LoggingFlags* pFlags) override
        {
            if (!ptsPath || !pFlags)
            {
                return E_POINTER;
            }

            *ptsPath = CalculateActualPath(m_tsPath);
            *pFlags = m_flags;

            return S_OK;
        }
    };

    class CTestLoggerService :
        public CLoggerService
    {
    private:
        tstring m_tsFilePath;
        LoggingFlags m_fileFlags;

    public:
        CTestLoggerService() :
            m_fileFlags(LoggingFlags_None)
        {
        }

    public:
        void SetFileFlags(LoggingFlags fileFlags)
        {
            m_fileFlags = fileFlags;
        }

        void SetFilePath(const tstring& tsPath)
        {
            m_tsFilePath = tsPath;
        }

    protected:
        HRESULT CreateSinks(std::vector<std::shared_ptr<ILoggerSink>>& sinks) override
        {
            sinks.push_back(make_shared<CDebugLoggerSink>());
            sinks.push_back(make_shared<CTestEventLoggerSink>());
            sinks.push_back(make_shared<CTestFileLoggerSink>(m_fileFlags, m_tsFilePath));
            sinks.push_back(make_shared<CHostLoggerSink>());
        }
    };
}