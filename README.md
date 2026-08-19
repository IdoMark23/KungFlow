# KungFlow
INCLUDED IN THIS GIT:
PRD file - [PRD](docs/product/PRD.docx). 
Presentation - [KungFlow.pdf](docs/presentation/KungFlow.pdf).
Internal experiment - [Kungflow_experiment](docs/research/Kungflow_experiment.docx).

## Local Development Startup

For Windows local development with SQL Server LocalDB:

```powershell
.\scripts\setup-local-db.ps1
```

Start the local server:

```powershell
.\scripts\start-local-server.ps1
```

Start the desktop app:

```powershell
.\scripts\start-desktop-ui.ps1
```

If PowerShell blocks script execution, run the same script through:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-local-server.ps1
```

Local mode is explicit. The local server script sets `KUNGFLOW_DB_MODE=local`, `SQLSERVER_SERVER=(localdb)\MSSQLLocalDB`, and `SQLSERVER_DRIVER=ODBC Driver 17 for SQL Server`. Cloud/deployment runs should not set `KUNGFLOW_DB_MODE=local`.

RESEARCH SOURCES WE BASED OUR WORK ON:
Problem Validation:

Archie, 2026: 77% of employees experienced work-related stress in the past month - https://archieapp.co/blog/workplace-statistics/
SpeakwiseApp, 2026: Additional data on stress, burnout, cognitive load, and workplace burnout - https://speakwiseapp.com/blog/employee-burnout-statistics
Livegroup, 2026: 68% report losing work time due to lack of focus and interruptions. The report also shows the link between alert frequency, real-time overload, focus ability, and burnout - https://143889941.fs1.hubspotusercontent-eu1.net/hubfs/143889941/Downloadable%20Resources/Livegroup/Attention%20Economy%20Report%202026.pdf
Insightful.io, 2025: Poor communication and excessive demands from employees raise stress and burnout levels - https://www.insightful.io/reports/stress-at-work
PMC, 2018: The impact of stress on health - https://pmc.ncbi.nlm.nih.gov/articles/PMC8368405/
Meditopia, 2026: The economic costs of employee stress for businesses - https://meditopia.com/en/forwork/articles/workplace-stress-statistics
McKinsey, 2023: Cognitive load and stress lead to poor decision-making - https://www.mckinsey.com.br/our-insights/bias-busters-how-cognitive-overload-multiplies-every-bias

Solution Validation:

HCI International, 2018: Movement alerts as an indicator of cognitive load - https://journals.sagepub.com/doi/10.1177/1541931218621449
ACM, 2013: Activity analysis as an indicator for identifying workload - https://dl.acm.org/doi/10.1145/2541016.2541083
PMC, 2023: Reducing interruptions improves performance - https://pmc.ncbi.nlm.nih.gov/articles/PMC10244611/
ResearchGate, 2010: Contex switching in a way of changing between tabs increases cognitive load - https://www.researchgate.net/publication/221515310_A_Study_of_Tabbed_Browsing_Among_Mozilla_Firefox_Users
ScienceDirect, 2009: Typing patterns as an indicator of stress - https://www.sciencedirect.com/science/article/abs/pii/S1071581909000937
Internal experiment - [Kungflow_experiment](docs/research/Kungflow_experiment.docx) in this git.

Existing Products:

Focus Bear - https://www.focusbear.io/
Freedom / Opal - https://freedom.to/
Somareality - https://somareality.com/
