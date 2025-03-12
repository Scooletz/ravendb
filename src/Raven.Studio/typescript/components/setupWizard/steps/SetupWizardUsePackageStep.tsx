import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";
import FileDropzone from "components/common/FileDropzone";
import { FormGroup, FormLabel, FormSelect } from "components/common/Form";
import { useAsync } from "react-async-hook";
import SetupWizardService from "components/services/SetupWizardService";
import { useServices } from "components/hooks/useServices";

export function SetupWizardUsePackageStep() {
    const { control } = useFormContext<SetupWizardFormData>();
    const {
        usePackageStep: { fileZip },
    } = useWatch({ control });

    const { setupWizardService } = useServices();

    // TODO add what is this tooltip

    const asyncExtractNodesInfoFromPackage = useAsync(
        () => setupWizardService.extractNodesInfoFromPackage(fileZip),
        [fileZip]
    );

    const handleFileChange = (files: File[]) => {
        console.log(files);
    };

    return (
        <div>
            <h2>Use setup package</h2>
            <p>Here you can use an existing package to set up selected nodes in your cluster.</p>
            <FormGroup>
                <FileDropzone onChange={handleFileChange} validExtensions={["zip"]} maxFiles={1} />
            </FormGroup>
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    <div>Node tag</div>
                    <div className="text-info">
                        <Icon icon="info" />
                        What is this?
                    </div>
                </FormLabel>
                <FormSelect
                    control={control}
                    name="usePackageStep.nodeTag"
                    options={[{ value: "A", label: "A" }]} // TODO add options
                    placeholder="Select node tag"
                    isLoading={asyncExtractNodesInfoFromPackage.loading}
                />
            </FormGroup>
        </div>
    );
}

export function SetupWizardUsePackageStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Summary");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
