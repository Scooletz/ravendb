import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardNodeAddressStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { nodeAddressStep } = useWatch({ control });

    return <h1>Node address</h1>;
}
