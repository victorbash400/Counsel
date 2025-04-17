# Counsel: AI-Powered Legal Assistant

![Counsel Logo](https://via.placeholder.com/800x150?text=Counsel:+AI-Powered+Legal+Assistant)

## 📖 Table of Contents
- [Project Overview](#project-overview)
- [Key Features](#key-features)
- [Architecture](#architecture)
  - [Backend Architecture](#backend-architecture)
  - [Frontend Architecture](#frontend-architecture)
  - [Communication Flow](#communication-flow)
- [Project Structure](#project-structure)
- [Core Components](#core-components)
  - [Semantic Kernel Integration](#semantic-kernel-integration)
  - [Plugin System](#plugin-system)
  - [Orchestration Service](#orchestration-service)
  - [Azure Services Integration](#azure-services-integration)
  - [Natural Language Calendar Generation](#natural-language-calendar-generation)
- [Operating Modes](#operating-modes)
  - [Chat Only](#chat-only)
  - [Deep Research](#deep-research)
  - [Paralegal](#paralegal)
  - [Cross-Examine](#cross-examine)
- [Installation & Setup](#installation--setup)
- [Usage Examples](#usage-examples)
- [Technologies Used](#technologies-used)
- [Future Enhancements](#future-enhancements)
- [Contributing](#contributing)
- [License](#license)

## Project Overview

Counsel is an intelligent legal assistant designed to revolutionize how legal professionals interact with their documents, research, and case management. Built for the Microsoft AI Agents Hackathon, Counsel leverages the power of Semantic Kernel, Azure OpenAI, and Azure Cognitive Search to deliver an intuitive, mode-based interface that adapts to different legal workflows.

The application provides specialized tooling for document analysis, legal research, evidence examination, and collaborative paralegal tasks—all accessible through a clean WPF interface that features a dual-pane layout for conversations and detailed results.

## Key Features

- **Multi-modal Interaction**: Switch seamlessly between different operational modes (Chat, Research, Paralegal, Cross-Examine) to handle diverse legal tasks.
- **Intelligent Document Processing**: Analyze, summarize, and extract key information from legal documents.
- **RAG-Enhanced Responses**: Retrieve and integrate relevant knowledge from document repositories to enhance AI responses.
- **Natural Language Calendar Generation**: Create calendar events (.ics files) from natural language descriptions.
- **Interactive UI**: Intuitive WPF application with animated sidebars, message history, and specialized canvas for detailed content.
- **Adaptive Plugin Architecture**: Domain-specific plugins extend functionality for research, examination, and paralegal tasks.
- **Context-Aware Conversations**: Maintains chat history to provide coherent, contextually relevant responses.
- **Dual-Pane Layout**: Separation between conversational interface and detailed content display.

## Architecture

### Backend Architecture

The Counsel backend is built on ASP.NET Core (.NET 8) with a service-oriented architecture that leverages dependency injection to coordinate diverse components:

![Backend Architecture](https://via.placeholder.com/800x400?text=Backend+Architecture+Diagram)

1. **Core Services Layer**:
   - Document processing for parsing and analyzing legal texts
   - Date resolution for temporal analysis
   - Calendar generation using Ical.Net

2. **AI Integration Layer**:
   - Azure OpenAI client for chat completions
   - Text embedding generation for semantic search
   - Semantic Kernel for orchestrating AI capabilities

3. **Plugin Layer**:
   - Specialized plugins for legal research, document examination, and paralegal tasks
   - Each plugin encapsulates domain-specific logic and interactions

4. **Orchestration Layer**:
   - SKOrchestratorService for coordinating plugins, services, and modes
   - Manages query routing, fallback strategies, and response generation

5. **API Layer**:
   - RESTful endpoints for frontend communication
   - Middleware for error handling, security, and search index management

### Frontend Architecture

The frontend is a WPF application with a clean separation of UI and logic:

![Frontend Architecture](https://via.placeholder.com/800x400?text=Frontend+Architecture+Diagram)

1. **UI Components**:
   - Conversation panel for displaying message bubbles
   - Right sidebar (canvas) for detailed content
   - Left sidebar for navigation and case management
   - Mode selection buttons for switching between operational contexts

2. **Input Handling**:
   - Text input with dynamic placeholder
   - File upload and document management
   - Mode switching and UI state management

3. **Communication**:
   - HTTP client for backend API interaction
   - Request/response models for structured data exchange

4. **Animation & Styling**:
   - Sidebar animations for smooth transitions
   - Dynamic message bubble creation and styling
   - Scroll management for conversation history

### Communication Flow

The application follows a clean request-response cycle:

1. User inputs a query or uploads documents through the WPF interface
2. The frontend formats the request with the current mode and sends it to the backend
3. The SKOrchestratorService routes the request to appropriate plugins based on the mode
4. Plugins leverage Azure services and Semantic Kernel to process the request
5. Responses are generated with both conversational summaries and detailed content
6. The frontend displays the conversational response in the chat panel and detailed content in the canvas

## Project Structure

```
Counsel/
├── Counsel.BackendApi/                # ASP.NET Core Backend
│   ├── Program.cs                     # Entry point and DI configuration
│   ├── Controllers/                   # API endpoints
│   │   └── QueryController.cs         # Main API controller
│   ├── Models/                        # Data models
│   │   ├── QueryRequest.cs            # Request structure
│   │   └── QueryResponse.cs           # Response structure
│   ├── Plugins/                       # Domain-specific plugins
│   │   ├── ParalegalPlugin.cs         # Document note generation and analysis
│   │   ├── ResearchPlugin.cs          # Legal research capabilities
│   │   └── ExaminePlugin.cs           # Document examination and analysis
│   ├── Services/                      # Core services
│   │   ├── SKOrchestratorService.cs   # Central orchestration service
│   │   ├── DocumentProcessingService.cs # Document handling
│   │   ├── DateResolutionService.cs   # Date analysis and formatting
│   │   └── CalendarService.cs         # Calendar generation
│   └── Utils/                         # Helper utilities
│       └── SearchIndexHelper.cs       # Azure Search index management
│
├── Counsel.WpfClient/                 # WPF Frontend Application
│   ├── App.xaml                       # Application definition
│   ├── MainWindow.xaml                # UI layout definition
│   ├── MainWindow.xaml.cs             # UI logic implementation
│   ├── Models/                        # Client-side models
│   │   └── CounselModels.cs           # Shared models with backend
│   ├── Services/                      # Client services
│   │   └── ApiService.cs              # Backend communication
│   └── Resources/                     # UI resources
│       ├── Styles.xaml                # Application styling
│       └── Icons/                     # UI icons
│
└── Counsel.Tests/                     # Test projects
    ├── BackendTests/                  # Backend unit tests
    └── IntegrationTests/              # End-to-end tests
```

## Core Components

### Semantic Kernel Integration

Counsel leverages Microsoft's Semantic Kernel as a foundational framework for AI capabilities:

- **Kernel Configuration**: Initialized in Program.cs with Azure OpenAI integration.
- **Chat Completion**: Configured with Azure OpenAI deployment for conversational responses.
- **Text Embedding**: Generates vector embeddings for document similarity searches.
- **Prompt Templates**: Custom prompts for specialized tasks like intent detection and summary generation.

The Semantic Kernel instance is injected into the orchestrator and plugins, providing a consistent AI interface throughout the application.

### Plugin System

Counsel implements a modular plugin architecture that encapsulates domain-specific functionality:

#### ParalegalPlugin

The ParalegalPlugin specializes in document analysis and processing:

```csharp
// Key methods in ParalegalPlugin
public async Task<string> GenerateDocNotesAsync(string[] documentChunks, string query)
public async Task<string> SummarizeContextAsync(string[] documentChunks, string query)
public async Task<string> ExtractKeyInfoAsync(string[] documentChunks, string query)
```

This plugin provides structured notes, summaries, and entity extraction from legal documents based on user queries and context.

#### ResearchPlugin

The ResearchPlugin focuses on legal research and knowledge retrieval:

```csharp
// Key method in ResearchPlugin
public async Task<string> PerformLegalResearchAsync(string query)
```

It leverages Azure Search and embedding-based similarity to retrieve relevant legal information and generate comprehensive research briefs.

#### ExaminePlugin

The ExaminePlugin specializes in cross-examination and critical analysis:

```csharp
// Key method in ExaminePlugin
public async Task<string> FindRelevantPassagesAsync(string query)
```

This plugin identifies inconsistencies, contradictions, and key passages in document collections for evidence analysis.

### Orchestration Service

The `SKOrchestratorService` is the central coordination component that:

1. **Routes Queries**: Directs user queries to appropriate plugins based on the selected mode.
2. **Manages Sessions**: Maintains conversation history for contextual understanding.
3. **Handles Fallbacks**: Implements graceful degradation when specialized processing fails.
4. **Generates Responses**: Creates both conversational summaries and detailed canvas content.

Key orchestration methods include:

```csharp
// Core orchestration method
public async Task<QueryResponse> ProcessQueryAsync(QueryRequest request)

// Mode-specific methods
private async Task<QueryResponse> ExecuteResearchModeAsync(QueryRequest request)
private async Task<QueryResponse> ExecuteParalegalModeAsync(QueryRequest request)
private async Task<QueryResponse> ExecuteExamineModeAsync(QueryRequest request)
private async Task<QueryResponse> ExecuteEnhancedChatAsync(QueryRequest request, bool isFallback = false, string fallbackPrefix = null)
```

The orchestrator is designed for resilience with error handling, fallback chains, and context preservation.

### Azure Services Integration

Counsel integrates with several Azure services:

- **Azure OpenAI**: Powers chat completions and embeddings generation.
- **Azure Cognitive Search**: Enables vector search for document retrieval.
- **Azure Application Insights**: Provides monitoring and logging (configured via Program.cs).

The integration points are abstracted through service wrappers and injected via dependency injection.

### Natural Language Calendar Generation

One of Counsel's standout features is the ability to generate calendar events from natural language descriptions:

1. The user describes an event in natural language (e.g., "Schedule a client meeting next Thursday at 2pm")
2. The backend parses this using the DateResolutionService to extract temporal data
3. The Calendar Generation service creates a standardized iCalendar (.ics) file using Ical.Net
4. The file is returned to the client for saving to the local system

This feature streamlines appointment and deadline management for legal professionals.

## Operating Modes

Counsel operates in four distinct modes, each tailored for specific legal workflows:

### Chat Only

The default conversational mode provides:
- General legal assistant capabilities
- Context-aware responses using session history
- Professional legal terminology and principles

### Deep Research

Research mode activates specialized legal research functionality:
- Comprehensive analysis of legal questions
- Citation of relevant principles and precedents
- Structured research briefs displayed in the canvas

### Paralegal

Paralegal mode offers advanced document processing:
- Intelligent task selection (notes, summarize, extract) based on query intent
- RAG-powered document retrieval using vector similarity
- Structured output formats for legal documentation

### Cross-Examine

Cross-examination mode specializes in critical analysis:
- Finding inconsistencies in evidence or testimony
- Identifying relevant passages for questioning
- Highlighting contradictions or weaknesses in documentation

## Installation & Setup

### Prerequisites

- .NET 8.0 SDK
- Visual Studio 2022 or later
- Azure subscription with OpenAI and Cognitive Search services

### Configuration

1. Clone the repository
2. Update the `appsettings.json` with your Azure service credentials:

```json
{
  "AzureOpenAI": {
    "Endpoint": "your-openai-endpoint",
    "ApiKey": "your-openai-key",
    "DeploymentName": "your-deployment-name"
  },
  "AzureSearch": {
    "Endpoint": "your-search-endpoint",
    "ApiKey": "your-search-key",
    "IndexName": "legal-documents"
  }
}
```

3. Build and run the application:

```bash
dotnet build
dotnet run --project Counsel.BackendApi
```

4. Launch the WPF client from Visual Studio or via:

```bash
dotnet run --project Counsel.WpfClient
```

## Usage Examples

### Legal Research Scenario

1. Select the "Deep Research" mode
2. Enter a query like "What are the key precedents for fair use in copyright law?"
3. The system will generate a comprehensive research brief in the canvas
4. Review the conversational summary in the chat window
5. Save or continue the conversation with follow-up questions

### Document Analysis Scenario

1. Upload legal documents using the attachment button
2. Select "Paralegal" mode
3. Ask "Extract all dates and monetary amounts from these contracts"
4. The system will identify the intent as "extract" and process accordingly
5. Review extracted entities in the structured format in the canvas

### Calendar Event Creation

1. Type "Schedule a deposition with Dr. Smith next Tuesday at 10 AM"
2. Click the calendar icon to generate an .ics file
3. Save the file and add it to your calendar application

## Technologies Used

- **Backend**:
  - ASP.NET Core (.NET 8)
  - Semantic Kernel
  - Azure OpenAI Service
  - Azure Cognitive Search
  - Ical.Net for calendar generation

- **Frontend**:
  - WPF (Windows Presentation Foundation)
  - XAML for UI definition
  - Custom animations and UI elements

- **Development Tools**:
  - Visual Studio 2022
  - Azure Developer CLI
  - .NET Test Framework

## Future Enhancements

- **Multi-platform Support**: Extend to web and mobile interfaces
- **Document Generation**: Create legal documents from templates and AI suggestions
- **Timeline Visualization**: Interactive timelines for case events and deadlines
- **Voice Interface**: Add speech recognition and response capabilities
- **Collaborative Features**: Real-time sharing and collaboration tools
- **Integration Ecosystem**: Connect with legal practice management systems

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details.