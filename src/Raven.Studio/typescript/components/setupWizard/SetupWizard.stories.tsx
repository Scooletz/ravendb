import { withForceRerender } from "test/storybookTestUtils";
import { withBootstrap5 } from "test/storybookTestUtils";
import { Meta, StoryObj } from "@storybook/react/*";
import { withStorybookContexts } from "test/storybookTestUtils";
import SetupWizard from "./SetupWizard";
import { mockServices } from "test/mocks/services/MockServices";
import { userEvent } from "@storybook/test";

export default {
    title: "Setup Wizard",
    decorators: [withStorybookContexts, withBootstrap5, withForceRerender],
} satisfies Meta;

export const Eula: StoryObj = {
    render: () => {
        const { setupWizardService } = mockServices;

        setupWizardService.withEula();

        return (
            <div style={{ height: 800 }}>
                <SetupWizard />
            </div>
        );
    },
};

export const SetupMethod: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await userEvent.click(canvas.queryByRole("button", { name: /Continue/ }));
    },
};

export const LicenseKey: StoryObj = {
    ...SetupMethod,
    play: async ({ canvas }) => {
        await userEvent.click(canvas.queryByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.queryByRole("heading", { name: /Set up new cluster/ }));
        await userEvent.click(canvas.queryByRole("button", { name: /Continue/ }));
    },
};

export const Security: StoryObj = {
    ...LicenseKey,
    play: async ({ canvas }) => {
        await userEvent.click(canvas.queryByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.queryByRole("heading", { name: /Set up new cluster/ }));
        await userEvent.click(canvas.queryByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.queryByRole("button", { name: /Continue/ }));
    },
};
