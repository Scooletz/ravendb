import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import SetupWizardClickableCard from "../partials/SetupWizardClickableCard";

export default function SetupWizardSetupMethodStep() {
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
                <span>I&apos;m just starting</span>
                <SetupWizardClickableCard
                    icon="server"
                    title="Set up new cluster"
                    description="Create a completely new cluster with fresh configurations"
                    method="newCluster"
                    isSelected={selectedMethod === "newCluster"}
                    onClick={() => setValue("setupMethodStep.method", "newCluster")}
                />
                <div className="mt-2">
                    <SetupWizardClickableCard
                        icon="default"
                        title="Create package for external setup"
                        description="Generate an external setup package during configuration for customized deployment"
                        method="createPackage"
                        isSelected={selectedMethod === "createPackage"}
                        onClick={() => setValue("setupMethodStep.method", "createPackage")}
                    />
                </div>
            </div>
            <div className="mt-4">
                <span>I have some to working with</span>
                <SetupWizardClickableCard
                    icon="default"
                    addon="arrow-up"
                    title="Use setup package"
                    description="Deploy the cluster using a predefined setup package with default or minimal configurations"
                    method="usePackage"
                    isSelected={selectedMethod === "usePackage"}
                    onClick={() => setValue("setupMethodStep.method", "usePackage")}
                />
            </div>
        </div>
    );
}
