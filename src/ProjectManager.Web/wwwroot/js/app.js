// Confirmação de ações destrutivas (ADR-015): formulários com data-confirm abrem o diálogo nativo antes de enviar.
// Roda na fase de captura, antes da navegação aprimorada do Blazor, e reenvia o formulário depois da confirmação.
const dialog = () => document.getElementById("pm-confirm");

document.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.dataset.confirm || form.dataset.confirmed === "true") {
        delete form.dataset?.confirmed;
        return;
    }

    const modal = dialog();
    if (!modal || typeof modal.showModal !== "function") {
        return;
    }

    event.preventDefault();
    event.stopImmediatePropagation();

    modal.querySelector("#pm-confirm-message").textContent = form.dataset.confirm;
    modal.querySelector("#pm-confirm-ok").textContent = form.dataset.confirmAction || "Confirmar";
    const submitter = event.submitter;
    modal.returnValue = "";
    modal.addEventListener("close", () => {
        if (modal.returnValue === "confirm") {
            form.dataset.confirmed = "true";
            form.requestSubmit(submitter && form.contains(submitter) ? submitter : undefined);
        }
    }, { once: true });
    modal.showModal();
}, true);
