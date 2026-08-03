import bpy
import json
import os


def _principled_bsdf(material):
    if not material.use_nodes or material.node_tree is None:
        return None

    for node in material.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            return node
    return None


def _material_record(material):
    principled = _principled_bsdf(material)
    if principled is None:
        color = material.diffuse_color
        metallic = getattr(material, "metallic", 0.0)
        roughness = getattr(material, "roughness", 0.5)
    else:
        color = principled.inputs["Base Color"].default_value
        metallic = principled.inputs["Metallic"].default_value
        roughness = principled.inputs["Roughness"].default_value

    return {
        "name": material.name,
        "baseColor": [float(color[0]), float(color[1]), float(color[2]), float(color[3])],
        "metallic": float(metallic),
        "roughness": float(roughness),
    }


def export_selected_fbx_with_materials():
    if not bpy.data.filepath:
        raise RuntimeError(".blend 파일이 저장되지 않았습니다. 먼저 Blender 파일을 저장해 주세요.")

    selected_objects = list(bpy.context.selected_objects)
    if not selected_objects:
        raise RuntimeError("선택된 오브젝트가 없습니다. 내보낼 오브젝트를 선택해 주세요.")

    blend_path = bpy.data.filepath
    output_directory = os.path.dirname(blend_path)
    model_name = os.path.splitext(os.path.basename(blend_path))[0]
    fbx_filename = model_name + ".fbx"
    json_filename = model_name + ".materials.json"
    fbx_path = os.path.join(output_directory, fbx_filename)
    json_path = os.path.join(output_directory, json_filename)

    materials = {}
    for obj in selected_objects:
        data = getattr(obj, "data", None)
        for material in getattr(data, "materials", []) if data is not None else []:
            if material is not None:
                materials[material.name] = material

    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        object_types={"MESH", "EMPTY", "ARMATURE"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
    )

    sidecar = {
        "version": 1,
        "fbx": fbx_filename,
        "materials": [_material_record(material) for material in materials.values()],
    }
    with open(json_path, "w", encoding="utf-8") as json_file:
        json.dump(sidecar, json_file, ensure_ascii=False, indent=2)

    print("FBX 및 재질 사이드카 내보내기 완료:", fbx_path, json_path)


export_selected_fbx_with_materials()
