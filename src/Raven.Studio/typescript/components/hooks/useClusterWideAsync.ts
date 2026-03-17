import { useAppSelector } from "components/store";
import { useCallback, useMemo, useReducer } from "react";
import { loadableData } from "components/models/common";
import assertUnreachable from "components/utils/assertUnreachable";
import { produce } from "immer";
import { useAsync } from "react-async-hook";
import DatabaseUtils from "components/utils/DatabaseUtils";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";

export interface nodeAwareLoadableData<T> extends loadableData<T> {
    nodeTag: string;
    shard?: number;
}

interface ClusterWideReducerState<T> {
    result: nodeAwareLoadableData<T>[];
}

export function useClusterWideAsync<T>(perNodeProvider: (location: databaseLocationSpecifier) => Promise<T>) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const locations = useMemo(() => DatabaseUtils.getLocations(db), [db]);

    const [state, dispatch] = useReducer(clusterWideReducer<T>, locations, initReducer<T>);

    const handleLocation = useCallback(
        async (location: databaseLocationSpecifier) => {
            try {
                const result = await perNodeProvider(location);
                dispatch({
                    type: "NodeDataLoaded",
                    nodeTag: location.nodeTag,
                    shard: location.shardNumber,
                    data: result,
                });
            } catch (error) {
                dispatch({
                    type: "NodeDataError",
                    nodeTag: location.nodeTag,
                    shard: location.shardNumber,
                    error,
                });
            }
        },
        [perNodeProvider]
    );

    const { execute, loading } = useAsync(
        () => Promise.allSettled(locations.map(handleLocation)),
        [locations, handleLocation]
    );

    return {
        result: state.result,
        refresh: execute,
        loading,
    };
}

function initReducer<T>(locations: databaseLocationSpecifier[]): ClusterWideReducerState<T> {
    return {
        result: locations.map(
            (location): nodeAwareLoadableData<T> => ({
                nodeTag: location.nodeTag,
                shard: location.shardNumber,
                data: undefined,
                status: "loading",
                error: undefined,
            })
        ),
    };
}

type ClusterWideReducerAction<T> = ActionNodeDataLoaded<T> | ActionNodeDataError;

interface ActionNodeDataLoaded<T> {
    nodeTag: string;
    shard?: number;
    type: "NodeDataLoaded";
    data: T;
}

interface ActionNodeDataError {
    nodeTag: string;
    shard?: number;
    type: "NodeDataError";
    error: any;
}

function clusterWideReducer<T>(
    state: ClusterWideReducerState<T>,
    action: ClusterWideReducerAction<T>
): ClusterWideReducerState<T> {
    const type = action.type;
    switch (type) {
        case "NodeDataLoaded":
            return produce(state, (draft) => {
                const itemToModify = draft.result.find((t) => t.nodeTag === action.nodeTag && t.shard === action.shard);
                if (!itemToModify) {
                    throw new Error("Unable to find data for node = " + action.nodeTag);
                }
                itemToModify.status = "success";
                itemToModify.data = action.data as any;
                itemToModify.error = undefined;
            });
        case "NodeDataError":
            return produce(state, (draft) => {
                const itemToModify = draft.result.find((t) => t.nodeTag === action.nodeTag && t.shard === action.shard);
                if (!itemToModify) {
                    throw new Error("Unable to find data for node = " + action.nodeTag);
                }
                itemToModify.status = "failure";
                itemToModify.data = undefined;
                itemToModify.error = action.error;
            });
        default:
            assertUnreachable(type);
    }
}
