import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardDomainStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { domainStep } = useWatch({ control });

    return <h1>Domain</h1>;
}
