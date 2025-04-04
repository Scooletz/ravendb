import { withForceRerender } from "test/storybookTestUtils";
import { withBootstrap5 } from "test/storybookTestUtils";
import { Meta, StoryObj } from "@storybook/react";
import { withStorybookContexts } from "test/storybookTestUtils";
import SetupWizard from "./SetupWizard";
import { mockServices } from "test/mocks/services/MockServices";
import { userEvent, waitFor, expect, waitForElementToBeRemoved } from "@storybook/test";
import { Canvas } from "storybook/internal/types";
import { SetupWizardSecurityOption, SetupWizardStepId } from "components/setupWizard/setupWizardValidation";

const getSecurityOptionLabel = (option: SetupWizardSecurityOption): string => {
    switch (option) {
        case "none":
            return "Don't use certificate";
        case "ownCertificate":
            return "Provide your own certificate";
        case "letsEncrypt":
            return "Generate Let’s Encrypt certificate";
    }
}

export default {
    title: "Setup Wizard",
    decorators: [withStorybookContexts, withBootstrap5, withForceRerender],
    argTypes: {
        licenseType: {
            control: {
                type: "select",
            },
            options: [
                "Community",
                "Developer",
                "Enterprise",
                "Essential",
                "Invalid",
                "None",
                "Professional",
                "Reserved",
            ] satisfies Raven.Server.Commercial.LicenseType[],
        },
        securityOption: {
            control: {
                type: "select",
                labels: {
                    letsEncrypt: "Generate Let's Encrypt certificate",
                    ownCertificate: "Provide your own certificate",
                    none: "Don't use certificate",
                }
            },
            options: [
                "none", "letsEncrypt", "ownCertificate",
            ] satisfies SetupWizardSecurityOption[],
        },
    },
    args: {
        securityOption: "letsEncrypt",
        licenseType: "Community",
    },
} satisfies Meta<SetupWizardStoryArgs>;

interface SetupWizardStoryArgs {
    licenseType: Raven.Server.Commercial.LicenseType;
    securityOption: SetupWizardSecurityOption;
}

export const Eula: StoryObj = {
    render: () => {
        const { setupWizardService } = mockServices;

        setupWizardService.withEula();
        setupWizardService.withNodesInfoFromPackage();
        setupWizardService.withRegistrationInfo();
        setupWizardService.withHostsForCertificate();
        setupWizardService.withGetSetupLocalNodeIps();
        setupWizardService.withGetSetupParameters();
        setupWizardService.withGetIpsInfo();
        setupWizardService.withCheckDomainAvailability();
        setupWizardService.withClaimDomain();
        setupWizardService.withLetsEncryptAgreement();

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
        await navigateToStep(canvas, "Setup method");
    },
};

export const UsePackage: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await navigateToStep(canvas, "Use setup package");
    },
};

export const LicenseKey: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "License key",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "License key", args);
    },
};

export const Security: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "Security", args);
    },
};

export const Domain: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "Domain", args);
    },
};

export const SelfSignedCertificate: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Self-signed certificate",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "Self-signed certificate", args);
    },
};

export const NodeAddresses: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Node addresses",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "Node address", args);
    },
};

export const AdditionalSettings: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Additional settings",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "Additional settings", args);
    },
};

export const Summary: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "Summary", args);
    },
};

export const Finish: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, "Finish", args);
    },
};


async function navigateToStep(canvas: Canvas, targetStep: SetupWizardStepId, args?: SetupWizardStoryArgs): Promise<void> {
    await waitForElementToBeRemoved(canvas.getByTestId("loader"));

    // If target is EULA, we're already there
    if (targetStep === "Eula") {
        return;
    }

    // Accept EULA
    const eula = await canvas.findByTestId("eula-bottom");
    await waitFor(() => expect(eula).toBeInTheDocument());
    eula.scrollIntoView({ behavior: "instant" });

    let continueButton = canvas.getByRole("button", { name: /Continue/ });
    await waitFor(() => expect(continueButton).not.toBeDisabled());
    await userEvent.click(continueButton);

    // If target is SETUP_METHOD, we're done
    if (targetStep === "Setup method") {
        return;
    }

    // Handle USE_PACKAGE path
    if (targetStep === "Use setup package") {
        await userEvent.click(canvas.getByRole("heading", { name: /Use setup package/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        return;
    }

    // For all other steps, go through Set up new cluster path
    await userEvent.click(canvas.getByRole("heading", { name: /Set up new cluster/ }));
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    // Enter license key
    if (args.licenseType) {
        await userEvent.type(
            canvas.getByTestId("license-key-input"),
            `{{ "Id": "${args.licenseType}", "Name": "RavenDB", "Keys": [[] }`
        );
    }

    if (targetStep === "License key") {
        return;
    }

    // Continue to SECURITY
    continueButton = canvas.getByRole("button", { name: /Continue/ });
    await waitFor(() => expect(continueButton).not.toBeDisabled());
    await userEvent.click(continueButton);

    if (targetStep === "Security") {
        return;
    }

    // Handle certificate paths
    if (targetStep === "Self-signed certificate" || args.securityOption === "ownCertificate") {
        await userEvent.click(canvas.getByRole("heading", { name: /Provide your own certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        
        const mockedCertificateFile = new File(["foo-bar"], "certificate.pfx");
        await userEvent.upload(await canvas.findByTestId("file-input"), mockedCertificateFile);
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        return;
    }

    // For remaining steps, go through Let's Encrypt certificate path
    await userEvent.click(canvas.getByRole("heading", { name: getSecurityOptionLabel(args.securityOption) }));
    if (args.securityOption === "letsEncrypt") {
        await userEvent.click(canvas.getByRole("checkbox"));
    }
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    if (targetStep === "Domain") {
        return;
    }

    // Continue to NODE_ADDRESSES
    if (args.securityOption !== "none") {
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    }

    if (targetStep === "Node address") {
        return;
    }

    // Save and continue to ADDITIONAL_SETTINGS
    await userEvent.click(canvas.getByRole("button", { name: /Save/ }));
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    if (targetStep === "Additional settings") {
        return;
    }

    // Continue to SUMMARY
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    if (targetStep === "Summary") {
        return;
    }

    // Finish the wizard
    await userEvent.click(canvas.getByRole("button", { name: /Finish/ }));
}
