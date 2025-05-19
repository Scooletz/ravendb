import { useAppDispatch, useAppSelector } from "components/store";
import { editGenAiTaskActions, editGenAiTaskSelectors } from "../store/editGenAiTaskSlice";
import AceEditor from "components/common/AceEditor";
import aceDiff from "common/helpers/text/aceDiff";
import ReactAce from "react-ace/lib/ace";
import { useRef, useEffect } from "react";
import { HStack } from "components/common/utilities/HStack";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import { VStack } from "components/common/utilities/VStack";
import { useEditGenAiTaskTests } from "../hooks/useEditGenAiTaskTests";
import ButtonWithSpinner from "components/common/ButtonWithSpinner";
import EditGenAiTaskReadOnlyVirtualList from "./EditGenAiTaskReadOnlyVirtualList";
import SizeGetter from "components/common/SizeGetter";
import Badge from "react-bootstrap/Badge";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { EmptySet } from "components/common/EmptySet";
import AceUnifiedDiff from "components/common/AceUnifiedDiff";
import { Switch } from "components/common/Checkbox";
import useBoolean from "components/hooks/useBoolean";

export default function EditGenAiTaskTestResults() {
    const dispatch = useAppDispatch();

    const currentStep = useAppSelector(editGenAiTaskSelectors.currentStep);
    const contextTest = useAppSelector(editGenAiTaskSelectors.contextTest);
    const modelInputTest = useAppSelector(editGenAiTaskSelectors.modelInputTest);

    const { handleContextTest, handleModelInputTest } = useEditGenAiTaskTests();

    return (
        <VStack className="test-results">
            {currentStep === "context" && (
                <>
                    <HStack className="mb-3 justify-content-between">
                        <h3 className="mb-0">
                            <Icon icon="test" />
                            Task context test results
                        </h3>
                        <HStack gap={2}>
                            <ButtonWithSpinner
                                variant="primary"
                                className="rounded-pill"
                                onClick={handleContextTest}
                                icon="reset"
                                isSpinning={contextTest.status === "loading"}
                            >
                                Re-run test
                            </ButtonWithSpinner>
                            <Button
                                variant="secondary"
                                className="rounded-pill"
                                onClick={() => dispatch(editGenAiTaskActions.isTestOpenSet(false))}
                            >
                                <Icon icon="cancel" />
                                Close
                            </Button>
                        </HStack>
                    </HStack>
                    {contextTest.data?.length === 0 ? (
                        <EmptySet>No results</EmptySet>
                    ) : (
                        <EditGenAiTaskReadOnlyVirtualList data={contextTest.data} />
                    )}
                </>
            )}
            {currentStep === "modelInput" && (
                <>
                    <HStack className="mb-3 justify-content-between">
                        <h3 className="mb-0">
                            <Icon icon="test" />
                            Model input test results
                        </h3>
                        <HStack gap={2}>
                            <ButtonWithSpinner
                                variant="primary"
                                className="rounded-pill"
                                icon="reset"
                                isSpinning={modelInputTest.status === "loading"}
                                onClick={handleModelInputTest}
                            >
                                Re-run test
                            </ButtonWithSpinner>
                            <Button
                                variant="secondary"
                                className="rounded-pill"
                                onClick={() => dispatch(editGenAiTaskActions.isTestOpenSet(false))}
                            >
                                <Icon icon="cancel" />
                                Close
                            </Button>
                        </HStack>
                    </HStack>
                    <ModelUsage />
                    {modelInputTest.data?.length === 0 ? (
                        <EmptySet>No results</EmptySet>
                    ) : (
                        <EditGenAiTaskReadOnlyVirtualList data={modelInputTest.data} />
                    )}
                </>
            )}
            {currentStep === "updateScript" && <UpdateScriptResult />}
        </VStack>
    );
}

function ModelUsage() {
    const modelUsage = useAppSelector(editGenAiTaskSelectors.modelUsage);

    if (modelUsage.status !== "success") {
        return null;
    }

    return (
        <div>
            <PopoverWithHoverWrapper
                message={
                    <div>
                        <HStack className="justify-content-between gap-3">
                            <span>Prompt tokens</span>
                            <span>{modelUsage.data.promptTokens}</span>
                        </HStack>
                        <HStack className="justify-content-between gap-3">
                            <span>Completion tokens</span>
                            <span>{modelUsage.data.completionTokens}</span>
                        </HStack>
                        <hr className="my-1" />
                        <HStack className="justify-content-between gap-3">
                            <span>Tokens usage</span>
                            <span>{modelUsage.data.totalTokens}</span>
                        </HStack>
                    </div>
                }
            >
                <Badge bg="info">
                    <Icon icon="info" />
                    Tokens usage: {modelUsage.data.totalTokens}
                </Badge>
            </PopoverWithHoverWrapper>
        </div>
    );
}

function UpdateScriptResult() {
    const dispatch = useAppDispatch();

    const updateScriptTest = useAppSelector(editGenAiTaskSelectors.updateScriptTest);
    const { handleUpdateScriptTest } = useEditGenAiTaskTests();

    const { value: isSplitDiff, toggle: toggleIsSplitDiff } = useBoolean(false);

    return (
        <>
            <HStack className="mb-3 justify-content-between">
                <h3 className="mb-0">
                    <Icon icon="test" />
                    Update script test results
                </h3>
                <HStack gap={2}>
                    <ButtonWithSpinner
                        variant="primary"
                        className="rounded-pill"
                        icon="reset"
                        isSpinning={updateScriptTest.status === "loading"}
                        onClick={handleUpdateScriptTest}
                    >
                        Re-run test
                    </ButtonWithSpinner>
                    <Button
                        variant="secondary"
                        className="rounded-pill"
                        onClick={() => dispatch(editGenAiTaskActions.isTestOpenSet(false))}
                    >
                        <Icon icon="cancel" />
                        Close
                    </Button>
                </HStack>
            </HStack>
            {updateScriptTest.data == null ? (
                <EmptySet>No results</EmptySet>
            ) : (
                <div className="flex-grow-1">
                    <HStack className="justify-content-between">
                        <div>Test result</div>
                        <Switch selected={isSplitDiff} toggleSelection={toggleIsSplitDiff} color="primary">
                            Split view
                        </Switch>
                    </HStack>
                    <SizeGetter
                        isHeighRequired
                        render={({ height }) =>
                            isSplitDiff ? (
                                <UpdateScriptDiffSplit height={height} />
                            ) : (
                                <UpdateScriptDiffUnified height={height} />
                            )
                        }
                    />
                </div>
            )}
        </>
    );
}

function UpdateScriptDiffSplit({ height }: { height: number }) {
    const updateScriptTest = useAppSelector(editGenAiTaskSelectors.updateScriptTest);

    const oldDoc = useAppSelector(editGenAiTaskSelectors.updateScriptDocumentInput);
    const newDoc = updateScriptTest.data ?? "";

    const oldDocRef = useRef<ReactAce>(null);
    const newDocRef = useRef<ReactAce>(null);

    useEffect(() => {
        if (!oldDocRef.current || !newDocRef.current) {
            return;
        }

        // We have different ace versions, lets just use 'any' here instead of editing aceDiff class
        const aceDiffC = new aceDiff(oldDocRef.current.editor as any, newDocRef.current.editor as any, false);
        aceDiffC.refresh(false);
    }, [oldDoc, newDoc]);

    if (oldDoc.status !== "success") {
        return null;
    }

    return (
        <VStack gap={2} className="update-script-result">
            <div className="diff-wrapper">
                <div className="diff-header">Original document</div>
                <AceEditor
                    aceRef={oldDocRef}
                    value={oldDoc.data}
                    mode="json"
                    height={`${height / 2 - 100}px`}
                    readOnly
                />
            </div>
            <div className="diff-wrapper">
                <div className="diff-header">Modified document</div>
                <AceEditor aceRef={newDocRef} value={newDoc} mode="json" height={`${height / 2 - 100}px`} readOnly />
            </div>
        </VStack>
    );
}

function UpdateScriptDiffUnified({ height }: { height: number }) {
    const updateScriptTest = useAppSelector(editGenAiTaskSelectors.updateScriptTest);

    const oldDoc = useAppSelector(editGenAiTaskSelectors.updateScriptDocumentInput);
    const newDoc = updateScriptTest.data ?? "";

    if (oldDoc.status !== "success") {
        return null;
    }

    return (
        <div className="diff-wrapper">
            <div className="diff-header">Modified document</div>
            <AceUnifiedDiff value1={oldDoc.data} value2={newDoc} height={`${height - 100}px`} mode="json" />
        </div>
    );
}
