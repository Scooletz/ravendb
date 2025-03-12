import { withForceRerender } from "test/storybookTestUtils";
import { withBootstrap5 } from "test/storybookTestUtils";
import { Meta, StoryObj } from "@storybook/react/*";
import { withStorybookContexts } from "test/storybookTestUtils";
import SetupWizard from "./SetupWizard";
import { mockServices } from "test/mocks/services/MockServices";
import { userEvent, waitFor, waitForElementToBeRemoved, expect } from "@storybook/test";
import { Canvas } from "storybook/internal/types";

export default {
    title: "Setup Wizard",
    decorators: [withStorybookContexts, withBootstrap5, withForceRerender],
} satisfies Meta;

export const Eula: StoryObj = {
    render: () => {
        const { setupWizardService } = mockServices;

        setupWizardService.withEula();
        setupWizardService.withNodesInfoFromPackage();

        return (
            <div style={{ height: 1000 }}>
                <SetupWizard />
            </div>
        );
    },
};

export const SetupMethod: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSetupStep(canvas);
    },
};

export const UsePackage: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSetupStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Use setup package/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const LicenseKey: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSetupStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Set up new cluster/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const Security: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSecurityStep(canvas);
    },
};

export const Domain: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSecurityStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const SelfSignedCertificate: StoryObj = {
    ...Eula,
    name: "Self-signed certificate",
    play: async ({ canvas }) => {
        await goToSecurityStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Provide your own certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const NodeAddresses: StoryObj = {
    ...Eula,
    name: "Node addresses",
    play: async ({ canvas }) => {
        await goToSecurityStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const AdditionalSettings: StoryObj = {
    ...Eula,
    name: "Additional settings",
    play: async ({ canvas }) => {
        await goToSecurityStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const Summary: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSecurityStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const Finish: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSecurityStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Finish/ }));
    },
};

async function goToSetupStep(canvas: Canvas) {
    await waitForElementToBeRemoved(canvas.getByTestId("loader"));

    const eula = document.getElementById("eula-bottom");
    eula.scrollIntoView({ behavior: "instant" });

    const continueButton = canvas.getByRole("button", { name: /Continue/ });
    await waitFor(() => expect(continueButton).not.toBeDisabled());

    await userEvent.click(continueButton);
}

async function goToSecurityStep(canvas: Canvas) {
    await goToSetupStep(canvas);
    await userEvent.click(canvas.getByRole("heading", { name: /Set up new cluster/ }));
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    await userEvent.type(canvas.getByTestId("license-key-input"), "some key");
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
}
