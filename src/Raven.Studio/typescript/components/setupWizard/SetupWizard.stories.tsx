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
        await goToSetupStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Set up new cluster/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        // TODO go next
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
