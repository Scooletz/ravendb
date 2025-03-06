import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import SetupWizardClickableCard from "../partials/SetupWizardClickableCard";
import { Button } from "react-bootstrap";
import { Icon } from "components/common/Icon";
import assertUnreachable from "components/utils/assertUnreachable";
import { ConditionalPopover } from "components/common/ConditionalPopover";

export function SetupWizardSetupMethodStep() {
    const { control, setValue } = useFormContext<SetupWizardFormData>();
    const {
        setupMethodStep: { method: selectedMethod },
    } = useWatch({ control });

    return (
        <div>
            <h2>Choose your setup method</h2>
            <p>
                This wizard will assist you with setting up your RavenDB server. You can set up a new cluster, create an
                external configuration package, or continue with an existing setup package.
            </p>
            <div className="mt-4">
                <h5 className="mb-1">I&apos;m just starting</h5>
                <SetupWizardClickableCard
                    icon="server"
                    title="Set up new cluster"
                    description="Create a completely new cluster with fresh configurations"
                    isSelected={selectedMethod === "newCluster"}
                    onClick={() => setValue("setupMethodStep.method", "newCluster")}
                />
                <SetupWizardClickableCard
                    className="mt-2"
                    icon="default"
                    title="Create package for external setup"
                    description="Generate an external setup package during configuration for customized deployment"
                    isSelected={selectedMethod === "createPackage"}
                    onClick={() => setValue("setupMethodStep.method", "createPackage")}
                />
            </div>
            <div className="mt-4">
                <h5 className="mb-1">I have some to working with</h5>
                <SetupWizardClickableCard
                    icon="default"
                    addon="arrow-up"
                    title="Use setup package"
                    description="Deploy the cluster using a predefined setup package with default or minimal configurations"
                    isSelected={selectedMethod === "usePackage"}
                    onClick={() => setValue("setupMethodStep.method", "usePackage")}
                />
            </div>
        </div>
    );
}

export function SetupWizardSetupMethodStepFooter() {
    const { control, setValue } = useFormContext<SetupWizardFormData>();

    const {
        setupMethodStep: { method: selectedMethod },
    } = useWatch({ control });

    const handleContinue = async () => {
        switch (selectedMethod) {
            case "newCluster":
            case "createPackage":
                setValue("currentStep", "License key");
                break;
            case "usePackage":
                setValue("currentStep", "Use setup package");
                break;
            default:
                assertUnreachable(selectedMethod);
        }
    };

    // TODO add popover

    return (
        <div className="d-flex justify-content-between">
            <small className="text-info">
                <Icon icon="info" />
                How can I setup manually?
            </small>
            <ConditionalPopover
                conditions={{
                    isActive: !selectedMethod,
                    message: "You need to complete this step to go forward.",
                }}
                popoverPlacement="top"
            >
                <Button variant="primary" className="rounded-pill" onClick={handleContinue} disabled={!selectedMethod}>
                    Continue <Icon icon="arrow-right" margin="m-0" />
                </Button>
            </ConditionalPopover>
        </div>
    );
}
