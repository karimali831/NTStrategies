from pathlib import Path
p=Path('src/Strategies/NinjexOvernightEdgePortfolio/ResearchTelemetry.cs')
s=p.read_text()

def r(old,new,label):
    global s
    c=s.count(old)
    if c!=1: raise RuntimeError(f'{label}: {c} matches')
    s=s.replace(old,new,1)

r('''        private string researchTelemetryPath = string.Empty;\n\n        private void InitializeResearchTelemetry()''','''        private string researchTelemetryPath = string.Empty;\n        private bool researchTelemetryFaulted;\n\n        private void InitializeResearchTelemetry()''','fault field')

r('''        private void InitializeResearchTelemetry()\n        {\n            DisposeResearchTelemetry();\n\n            if (!EnableResearchTelemetry)\n            {\n                researchSink = new NinjexOvernightEdgeNullResearchSink();\n                researchTelemetryPath = string.Empty;\n                return;\n            }\n\n            var directory = Path.Combine(Core.Globals.UserDataDir, "NinjexResearch", "OvernightEdgePortfolio");\n            Directory.CreateDirectory(directory);\n\n            var instrumentName = Instrument == null ? "UnknownInstrument" : SanitizeFileName(Instrument.FullName);\n            var fileName = string.Format(\n                CultureInfo.InvariantCulture,\n                "overnight_edge_research_{0}_{1}_{2}.csv",\n                instrumentName,\n                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture),\n                Guid.NewGuid().ToString("N").Substring(0, 8));\n\n            researchTelemetryPath = Path.Combine(directory, fileName);\n            researchSink = new NinjexOvernightEdgeCsvResearchSink(researchTelemetryPath);\n            Diagnostic(DateTime.Now, "RESEARCH TELEMETRY READY Path={0}", researchTelemetryPath);\n        }''','''        private void InitializeResearchTelemetry()\n        {\n            DisposeResearchTelemetry();\n            researchTelemetryFaulted = false;\n\n            if (!EnableResearchTelemetry)\n            {\n                researchSink = new NinjexOvernightEdgeNullResearchSink();\n                researchTelemetryPath = string.Empty;\n                return;\n            }\n\n            try\n            {\n                var directory = Path.Combine(Core.Globals.UserDataDir, "NinjexResearch", "OvernightEdgePortfolio");\n                Directory.CreateDirectory(directory);\n\n                var instrumentName = Instrument == null ? "UnknownInstrument" : SanitizeFileName(Instrument.FullName);\n                var fileName = string.Format(\n                    CultureInfo.InvariantCulture,\n                    "overnight_edge_research_{0}_{1}_{2}.csv",\n                    instrumentName,\n                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture),\n                    Guid.NewGuid().ToString("N").Substring(0, 8));\n\n                researchTelemetryPath = Path.Combine(directory, fileName);\n                researchSink = new NinjexOvernightEdgeCsvResearchSink(researchTelemetryPath);\n                Diagnostic(DateTime.Now, "RESEARCH TELEMETRY READY Path={0}", researchTelemetryPath);\n            }\n            catch (Exception ex)\n            {\n                researchTelemetryFaulted = true;\n                researchTelemetryPath = string.Empty;\n                researchSink = new NinjexOvernightEdgeNullResearchSink();\n                Diagnostic(DateTime.Now, "RESEARCH TELEMETRY DISABLED InitError={0}", ex.Message);\n            }\n        }''','init fail safe')

r('''        private void DisposeResearchTelemetry()\n        {\n            if (researchSink != null)\n                researchSink.Dispose();\n            researchSink = new NinjexOvernightEdgeNullResearchSink();\n        }''','''        private void DisposeResearchTelemetry()\n        {\n            try\n            {\n                if (researchSink != null)\n                    researchSink.Dispose();\n            }\n            catch\n            {\n                // Telemetry cleanup must never interfere with strategy lifecycle.\n            }\n            finally\n            {\n                researchSink = new NinjexOvernightEdgeNullResearchSink();\n            }\n        }''','dispose fail safe')

s=s.replace('if (!EnableResearchTelemetry)\n                return;', 'if (!EnableResearchTelemetry || researchTelemetryFaulted)\n                return;')

s=s.replace('researchSink.Write(row);', 'WriteResearchRow(row);')
s=s.replace('researchSink.Write(activeResearchSignal);', 'WriteResearchRow(activeResearchSignal);')

marker='''        private NinjexOvernightEdgeResearchRow CreateBaseResearchRow(DateTime time, string modelName, string signalName, PendingDirection direction)\n'''
if s.count(marker)!=1: raise RuntimeError('base row marker')
method='''        private void WriteResearchRow(NinjexOvernightEdgeResearchRow row)\n        {\n            if (row == null || !EnableResearchTelemetry || researchTelemetryFaulted)\n                return;\n\n            try\n            {\n                researchSink.Write(row);\n            }\n            catch (Exception ex)\n            {\n                researchTelemetryFaulted = true;\n                DisposeResearchTelemetry();\n                Diagnostic(\n                    row.EventTime == Core.Globals.MinDate ? DateTime.Now : row.EventTime,\n                    "RESEARCH TELEMETRY DISABLED WriteError={0}",\n                    ex.Message);\n            }\n        }\n\n'''
s=s.replace(marker, method+marker,1)

p.write_text(s)
print('hardened telemetry')
