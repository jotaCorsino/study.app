namespace studyhub.app.services;

public sealed class CourseIntentPromptService
{
    public async Task<OnlineCourseIntentPromptResult?> PromptAsync()
    {
        var page = ResolvePage();

        var topic = await MainThread.InvokeOnMainThreadAsync(() => page.DisplayPromptAsync(
            "Novo curso por IA",
            "O que voce quer aprender?",
            "Continuar",
            "Cancelar",
            "Ex.: ASP.NET Core para APIs reais"));

        if (string.IsNullOrWhiteSpace(topic))
        {
            return null;
        }

        var objective = await MainThread.InvokeOnMainThreadAsync(() => page.DisplayPromptAsync(
            "Objetivo do curso",
            "Qual resultado voce quer atingir com esse curso?",
            "Criar curso",
            "Cancelar",
            "Ex.: sair do basico e construir projetos guiados"));

        if (string.IsNullOrWhiteSpace(objective))
        {
            return null;
        }

        return new OnlineCourseIntentPromptResult(topic.Trim(), objective.Trim());
    }

    private static Page ResolvePage()
    {
        var page = Application.Current?
            .Windows
            .FirstOrDefault(window => window.Page != null)?
            .Page;

        return page ?? throw new InvalidOperationException("Nao foi possivel localizar a pagina ativa para coletar a intencao do curso.");
    }
}

public sealed record OnlineCourseIntentPromptResult(string Topic, string Objective);
